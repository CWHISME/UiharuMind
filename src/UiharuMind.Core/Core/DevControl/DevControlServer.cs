using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.DevControl;

/// <summary>
/// 一步的执行结果，与开发脚本报告的条目同形
/// </summary>
/// <param name="Ok">是否成功</param>
/// <param name="ElapsedMs">耗时</param>
/// <param name="Result">结果，可为 null</param>
/// <param name="Error">失败原因</param>
public sealed record DevStepOutcome(bool Ok, long ElapsedMs, object? Result = null, string? Error = null);

/// <summary>
/// 开发控制通道的服务端（ADR 0059）：本机 Unix socket，一行一个 JSON 请求，一行一个回复。
/// 请求各跑各的（一条在等群跑完时，另一条要能插话），同一连接的回复按完成先后写回、靠 <c>id</c> 对应。
/// 只认本次启动生成的令牌；<c>app.ping</c> 在这里答，其余交给 <see cref="_handler"/>
/// </summary>
public sealed class DevControlServer : IDisposable
{
    /// <summary>回复原样带中文：群流水全是中文，转义成 \uXXXX 就没法直接读</summary>
    public static readonly JsonSerializerOptions ReplyOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly DevControlEndpoint _endpoint;
    private readonly Func<string, JsonElement, Task<DevStepOutcome>> _handler;
    private readonly CancellationTokenSource _stop = new();
    private Socket? _listener;
    private byte[] _token = [];

    /// <param name="endpoint">通道位置</param>
    /// <param name="handler">执行一步：步骤名、参数 → 结果</param>
    public DevControlServer(DevControlEndpoint endpoint, Func<string, JsonElement, Task<DevStepOutcome>> handler)
    {
        _endpoint = endpoint;
        _handler = handler;
    }

    /// <summary>
    /// 开始监听。这个档案已有实例开着通道时不开
    /// </summary>
    /// <returns>开成了为 true</returns>
    /// <remarks>run 目录是符号链接、不归本用户、socket 路径超长（macOS 上限 104 字节）、绑定失败都会抛；调用方记一笔即可，不该拖垮启动</remarks>
    public bool Start()
    {
        PrepareRunDirectory();
        if (File.Exists(_endpoint.SocketPath))
        {
            if (IsAlive(_endpoint.SocketPath))
            {
                Log.Warning($"Dev control: '{_endpoint.SocketPath}' is held by another instance, not listening.");
                return false;
            }

            File.Delete(_endpoint.SocketPath); //上次没收尾的残留
        }

        // 先绑上再写令牌：绑不上（路径超长之类）就不留下一份没人用的令牌
        Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(_endpoint.SocketPath));
            listener.Listen(8);
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _token = Encoding.UTF8.GetBytes(token);
            WriteToken(token);
        }
        catch
        {
            listener.Dispose();
            if (File.Exists(_endpoint.SocketPath)) TryDelete(_endpoint.SocketPath);
            throw;
        }

        _listener = listener;
        _ = AcceptLoopAsync(_listener);
        Log.Debug($"Dev control: listening on '{_endpoint.SocketPath}'.");
        return true;
    }

    /// <summary>停止监听，删掉 socket 与令牌</summary>
    public void Dispose()
    {
        if (_stop.IsCancellationRequested) return;

        _stop.Cancel();
        _listener?.Dispose();
        if (_listener == null) return;

        TryDelete(_endpoint.SocketPath);
        TryDelete(_endpoint.TokenPath);
        Log.Debug("Dev control: stopped.");
    }

    private void PrepareRunDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(_endpoint.RunDirectory);
            return;
        }

        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Directory.CreateDirectory(_endpoint.RunDirectory, ownerOnly);
        // 档案放在 /tmp 这类人人可写的地方时，别人能抢先把 run 换成指向他处的链接
        if (new DirectoryInfo(_endpoint.RunDirectory).LinkTarget != null)
            throw new IOException($"'{_endpoint.RunDirectory}' is a symbolic link; refusing to open dev control there");
        File.SetUnixFileMode(_endpoint.RunDirectory, ownerOnly); //目录早就在时 CreateDirectory 不改权限；不是自己的目录这里会抛
    }

    private void WriteToken(string token)
    {
        FileStreamOptions options = new() { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (StreamWriter file = new(_endpoint.TokenPath, Encoding.UTF8, options)) file.Write(token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_endpoint.TokenPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); //文件早就在时创建模式不生效
    }

    private static bool IsAlive(string socketPath)
    {
        try
        {
            using Socket probe = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            probe.Connect(new UnixDomainSocketEndPoint(socketPath));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private async Task AcceptLoopAsync(Socket listener)
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                Log.Warning($"Dev control: accept failed: {e.Message}");
                continue;
            }

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(Socket client)
    {
        NetworkStream stream = new(client, ownsSocket: true);
        StreamReader reader = new(stream, Encoding.UTF8);
        StreamWriter writer = new(stream, new UTF8Encoding(false)) { AutoFlush = true };
        SemaphoreSlim writeLock = new(1, 1);

        try
        {
            while (await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false) is { } line)
            {
                if (line.Length == 0) continue;

                // 格式不对、令牌不对都回一句就断开
                bool parsed = TryParse(line, out JsonNode? id, out string? token, out string op, out JsonElement args);
                if (!parsed || !IsToken(token))
                {
                    await WriteAsync(writer, writeLock, Reply(id, new DevStepOutcome(false, 0, Error: parsed ? "bad token" : "malformed request")));
                    return;
                }

                _ = RunAsync(writer, writeLock, id, op, args);
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
            //客户端断开或通道关闭：已在跑的步骤照跑完，回复写不出去就算了
        }
        finally
        {
            // 拿着写锁再关：还在写回复的那位写完才关，之后的写会撞 ObjectDisposedException，在 RunAsync 里吞掉
            await writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
            }

            reader.Dispose();
            await stream.DisposeAsync().ConfigureAwait(false);
            writeLock.Release();
        }
    }

    private async Task RunAsync(StreamWriter writer, SemaphoreSlim writeLock, JsonNode? id, string op, JsonElement args)
    {
        DevStepOutcome outcome;
        if (op == "app.ping")
        {
            outcome = new DevStepOutcome(true, 0, new { pid = Environment.ProcessId });
        }
        else
        {
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                outcome = await _handler(op, args).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                outcome = new DevStepOutcome(false, watch.ElapsedMilliseconds, Error: $"{e.GetType().Name}: {e.Message}");
            }
        }

        string reply;
        try
        {
            reply = Reply(id, outcome);
        }
        catch (Exception e)
        {
            // 结果序列化不了也得回一句，否则客户端干等
            reply = Reply(id, new DevStepOutcome(false, outcome.ElapsedMs, Error: $"result not serializable: {e.GetType().Name}: {e.Message}"));
        }

        try
        {
            await WriteAsync(writer, writeLock, reply).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }

    private static async Task WriteAsync(StreamWriter writer, SemaphoreSlim writeLock, string line)
    {
        await writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private bool IsToken(string? token) =>
        token != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), _token);

    private static bool TryParse(string line, out JsonNode? id, out string? token, out string op, out JsonElement args)
    {
        id = null;
        token = null;
        op = string.Empty;
        args = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (root.TryGetProperty("id", out JsonElement idElement)) id = JsonNode.Parse(idElement.GetRawText());
            if (root.TryGetProperty("token", out JsonElement tokenElement)) token = tokenElement.GetString();
            if (!root.TryGetProperty("op", out JsonElement opElement) || opElement.GetString() is not { Length: > 0 } name)
                return false;

            op = name;
            if (root.TryGetProperty("args", out JsonElement argsElement)) args = argsElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Reply(JsonNode? id, DevStepOutcome outcome)
    {
        JsonObject reply = new()
        {
            ["id"] = id?.DeepClone(), //同一个 id 可能拼两次回复（第一次序列化失败），节点只能有一个父节点
            ["ok"] = outcome.Ok,
            ["elapsedMs"] = outcome.ElapsedMs,
        };
        if (outcome.Ok) reply["result"] = outcome.Result == null ? null : JsonSerializer.SerializeToNode(outcome.Result, ReplyOptions);
        else reply["error"] = outcome.Error;
        return reply.ToJsonString(ReplyOptions);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
            Log.Warning($"Dev control: deleting '{path}' failed: {e.Message}");
        }
    }
}
