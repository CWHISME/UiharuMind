using System.Net;
using System.Net.Sockets;
using CliWrap;
using CliWrap.EventStream;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// llama-server 出错：起不来（提前退出、就绪超时、找不到可执行文件）或跑着跑着退出了
/// </summary>
public sealed class LlamaServerException(string message, string logTail = "") : Exception(message)
{
    /// <summary>
    /// 进程最后几行输出，供用户判断原因（显存不够、文件损坏、驱动问题…）
    /// </summary>
    public string LogTail { get; } = logTail;

    /// <summary>
    /// 原因加日志尾巴，直接给用户看
    /// </summary>
    public string Detail => LogTail.Length == 0 ? Message : $"{Message}\n{LogTail}";
}

/// <summary>
/// 一个 llama-server 进程：自选空闲端口，轮询 /health 判就绪，起不来时带上日志尾巴如实失败。
/// 对话与嵌入共用。
/// </summary>
internal sealed class LlamaServerProcess
{
    private const int LogTailLines = 12;
    private const float ProgressLogLines = 128f; // 加载阶段大约输出的行数，只用来估个进度
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    private readonly Queue<string> _logTail = new();
    private readonly Lock _logGate = new();
    private int _exitCode = -1;

    private LlamaServerProcess(int port)
    {
        Port = port;
    }

    /// <summary>
    /// 监听端口（仅 127.0.0.1）
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// 服务根地址
    /// </summary>
    public Uri BaseUri => new($"http://127.0.0.1:{Port}/");

    /// <summary>
    /// 进程生命周期，进程退出或被取消时完成
    /// </summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// 退出码，未退出为 -1
    /// </summary>
    public int ExitCode => _exitCode;

    /// <summary>
    /// 启动并等到 /health 返回 200
    /// </summary>
    /// <param name="executablePath">llama-server 可执行文件</param>
    /// <param name="arguments">除 host/port 外的参数，逐项传入，不需要自己加引号</param>
    /// <param name="readyTimeout">就绪超时</param>
    /// <param name="onProgress">加载进度（按日志行数估算）</param>
    /// <param name="lifetimeToken">取消即结束进程</param>
    /// <param name="environment">额外环境变量</param>
    /// <returns>已就绪的服务</returns>
    /// <exception cref="LlamaServerException">进程提前退出或超时</exception>
    public static async Task<LlamaServerProcess> StartAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan readyTimeout,
        Action<float>? onProgress,
        CancellationToken lifetimeToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        if (!File.Exists(executablePath))
            throw new LlamaServerException($"llama-server executable not found: {executablePath}");

        LlamaServerProcess server = new(FindFreePort());
        CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        List<string> args = [..arguments, "--host", "127.0.0.1", "--port", server.Port.ToString()];
        Log.Debug($"Start llama-server: {string.Join(' ', args)}");

        server.Completion = Task.Run(() => server.RunAsync(executablePath, args, environment, onProgress, lifetime.Token),
            CancellationToken.None);

        try
        {
            await server.WaitUntilReadyAsync(readyTimeout, lifetime.Token).ConfigureAwait(false);
            onProgress?.Invoke(1f);
            return server;
        }
        catch
        {
            lifetime.Cancel();
            throw;
        }
        finally
        {
            // Cancel 之后才挂释放，避免释放与取消赛跑
            _ = server.Completion.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
        }
    }

    /// <summary>
    /// 向系统要一个当前空闲的本地端口
    /// </summary>
    /// <returns>端口号</returns>
    public static int FindFreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private async Task RunAsync(
        string executablePath,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? environment,
        Action<float>? onProgress,
        CancellationToken token)
    {
        int processId = 0;
        int lineCount = 0;
        try
        {
            await foreach (CommandEvent cmdEvent in Cli.Wrap(executablePath)
                               .WithArguments(args)
                               .WithEnvironmentVariables(environment?.ToDictionary(x => x.Key, x => (string?)x.Value) ??
                                                         new Dictionary<string, string?>())
                               .WithValidation(CommandResultValidation.None)
                               .ListenAsync(token)
                               .ConfigureAwait(false))
            {
                switch (cmdEvent)
                {
                    case StartedCommandEvent started:
                        processId = started.ProcessId;
                        ProcessHelper.Track(processId);
                        break;
                    case StandardOutputCommandEvent stdOut:
                        OnLog(stdOut.Text, ++lineCount, onProgress);
                        break;
                    case StandardErrorCommandEvent stdErr:
                        OnLog(stdErr.Text, ++lineCount, onProgress);
                        break;
                    case ExitedCommandEvent exited:
                        _exitCode = exited.ExitCode;
                        if (!token.IsCancellationRequested)
                            Log.Warning($"llama-server exited with code {exited.ExitCode}. {LogTail}");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            OnLog(e.Message, lineCount, null);
            Log.Error($"llama-server failed: {e.Message}");
        }
        finally
        {
            if (processId != 0) ProcessHelper.Untrack(processId);
        }
    }

    private void OnLog(string line, int lineCount, Action<float>? onProgress)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Log.Debug(line);
        lock (_logGate)
        {
            _logTail.Enqueue(line);
            if (_logTail.Count > LogTailLines) _logTail.Dequeue();
        }

        // 每 16 行报一次，封顶 0.95，真正的 1 留给就绪
        if (onProgress != null && lineCount % 16 == 0)
            onProgress(Math.Min(0.95f, lineCount / ProgressLogLines));
    }

    /// <summary>
    /// 最后几行输出
    /// </summary>
    public string LogTail
    {
        get
        {
            lock (_logGate) return string.Join('\n', _logTail);
        }
    }

    private async Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken token)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        Uri healthUri = new(BaseUri, "health");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (Completion.IsCompleted)
                throw new LlamaServerException(
                    $"llama-server exited before it was ready (exit code {_exitCode}).", LogTail);
            if (DateTime.UtcNow > deadline)
                throw new LlamaServerException(
                    $"llama-server was not ready within {timeout.TotalSeconds:0}s.", LogTail);

            if (await IsHealthyAsync(healthUri, token).ConfigureAwait(false)) return;
            await Task.WhenAny(Task.Delay(PollInterval, token), Completion).ConfigureAwait(false);
        }
    }

    // 加载中返回 503，端口还没开时连接被拒，都算「还没好」
    private static async Task<bool> IsHealthyAsync(Uri healthUri, CancellationToken token)
    {
        try
        {
            using HttpResponseMessage response = await HealthClient.GetAsync(healthUri, token).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!token.IsCancellationRequested)
        {
            return false;
        }
    }
}
