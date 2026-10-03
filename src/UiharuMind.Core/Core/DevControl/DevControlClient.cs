using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace UiharuMind.Core.Core.DevControl;

/// <summary>
/// 开发控制通道的客户端（ADR 0059）：连上档案里的 socket，带令牌一步一步发，一次等一个回复
/// </summary>
public sealed class DevControlClient : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly string _token;
    private long _nextId;

    private DevControlClient(Socket socket, string token)
    {
        _socket = socket;
        _token = token;
        NetworkStream stream = new(socket, ownsSocket: false);
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    /// <summary>
    /// 连上这个档案的通道
    /// </summary>
    /// <param name="endpoint">通道位置</param>
    /// <param name="token">取消</param>
    /// <returns>客户端</returns>
    /// <exception cref="InvalidOperationException">通道没开（应用没跑、没开开发者模式也没带 --dev-control）</exception>
    public static async Task<DevControlClient> ConnectAsync(DevControlEndpoint endpoint, CancellationToken token = default)
    {
        if (!File.Exists(endpoint.SocketPath) || !File.Exists(endpoint.TokenPath))
            throw new InvalidOperationException($"dev control is not open at '{endpoint.SocketPath}'");

        Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.SocketPath), token).ConfigureAwait(false);
            string secret = (await File.ReadAllTextAsync(endpoint.TokenPath, token).ConfigureAwait(false)).Trim();
            return new DevControlClient(socket, secret);
        }
        catch (Exception e) when (e is SocketException or IOException or UnauthorizedAccessException)
        {
            socket.Dispose();
            throw new InvalidOperationException($"dev control at '{endpoint.SocketPath}' refused: {e.Message}", e);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 发一步，等它的回复
    /// </summary>
    /// <param name="op">步骤名</param>
    /// <param name="args">参数；没有为 null</param>
    /// <param name="token">取消（只是不再等，应用那头照跑）</param>
    /// <returns>回复：<c>{"id","ok","elapsedMs","result"|"error"}</c></returns>
    public async Task<JsonObject> CallAsync(string op, JsonNode? args = null, CancellationToken token = default)
    {
        long id = Interlocked.Increment(ref _nextId);
        JsonObject request = new() { ["id"] = id, ["token"] = _token, ["op"] = op };
        if (args != null) request["args"] = args.DeepClone();
        await _writer.WriteLineAsync(request.ToJsonString().AsMemory(), token).ConfigureAwait(false);

        while (true)
        {
            string line = await _reader.ReadLineAsync(token).ConfigureAwait(false)
                          ?? throw new IOException("dev control closed the connection");
            if (JsonNode.Parse(line) is not JsonObject reply) continue;
            // 服务端拒掉格式不对的请求时回的 id 可能为空：那就是这一条（一次只等一条）
            JsonNode? replyId = reply["id"];
            if (replyId == null || (replyId is JsonValue value && value.TryGetValue(out long number) && number == id)) return reply;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync().ConfigureAwait(false);
        _reader.Dispose();
        _socket.Dispose();
    }
}
