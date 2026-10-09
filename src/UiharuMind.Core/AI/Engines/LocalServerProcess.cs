using System.Net;
using System.Net.Sockets;
using CliWrap;
using CliWrap.EventStream;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Engines;

/// <summary>
/// 本地引擎服务出错：起不来（提前退出、就绪超时、找不到可执行文件）或跑着跑着退出了
/// </summary>
public sealed class LocalServerException(string message, string logTail = "") : Exception(message)
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
/// 怎么起一个本地引擎服务
/// </summary>
/// <param name="ServerName">日志与报错里的名字，如 llama-server</param>
/// <param name="ExecutablePath">可执行文件</param>
/// <param name="BuildArguments">按分到的端口生成启动参数（逐项传入，不需要自己加引号）</param>
/// <param name="ReadyTimeout">就绪超时：按停滞算，启动日志还在动就接着等，这么久没新输出才算起不来</param>
public sealed record LocalServerLaunch(
    string ServerName,
    string ExecutablePath,
    Func<int, IReadOnlyList<string>> BuildArguments,
    TimeSpan ReadyTimeout)
{
    /// <summary>
    /// 额外环境变量
    /// </summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>
    /// 就绪探针路径，返回 200 即就绪
    /// </summary>
    public string HealthPath { get; init; } = "health";

    /// <summary>
    /// 加载阶段大约输出的行数，只用来估个进度
    /// </summary>
    public float ExpectedLogLines { get; init; } = 128f;

    /// <summary>
    /// 起来后大约占多少内存（字节），拉起前按此刻可用内存检查；0 为不估（启动时不加载模型的引擎）
    /// </summary>
    public long EstimatedMemoryBytes { get; init; }
}

/// <summary>
/// 一个本地引擎服务进程（sidecar）：自选空闲端口，轮询就绪探针，起不来时带上日志尾巴如实失败；
/// 随本进程退出（<see cref="ChildProcessBinding"/>），崩溃也不留残留。
/// llama-server、audiocpp_server 共用，引擎间的差别只在 <see cref="LocalServerLaunch"/>。
/// </summary>
public sealed class LocalServerProcess
{
    private const int LogTailLines = 12;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    private readonly LocalServerLaunch _launch;
    private readonly Queue<string> _logTail = new();
    private readonly Lock _logGate = new();
    private readonly Lock _sampleGate = new();
    private int _exitCode = -1;
    private int _launcherId; //直接拉起的进程：Windows 上就是服务本身，其余平台是看门的 /bin/sh
    private int _serverId; //服务本身，用到才找
    private long _lastActivity = Environment.TickCount64; //最近一次输出：大模型冷启动慢，只要还在动就不算超时
    private (TimeSpan CpuTime, long Ticks)? _lastSample; //上次取样，算 CPU 占比用

    private LocalServerProcess(LocalServerLaunch launch, int port)
    {
        _launch = launch;
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
    /// 服务进程本身的 Id（macOS / Linux 上隔着一层看门的 sh，第一次问时去找）；没起来或已退出为 null
    /// </summary>
    public int? ServerProcessId
    {
        get
        {
            if (Completion.IsCompleted || Volatile.Read(ref _launcherId) is not (> 0 and var launcher)) return null;
            if (_serverId > 0) return _serverId;
            if (OperatingSystem.IsWindows()) return _serverId = launcher;
            // 看门脚本先起服务、再起读生命线的子 shell，跳过名叫 sh 的那个
            foreach (int child in ProcessUsage.ChildrenOf(launcher))
            {
                try
                {
                    using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(child);
                    if (process.ProcessName != "sh") return _serverId = child;
                }
                catch (ArgumentException)
                {
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 取一次服务进程的实际占用（CPU 按与上次取样之间算）
    /// </summary>
    /// <returns>占用；没起来或已退出为 null</returns>
    public EngineUsage? SampleUsage()
    {
        if (ServerProcessId is not { } processId || ProcessUsage.TrySample(processId) is not { } sample) return null;

        long now = Environment.TickCount64;
        double? cpu = null;
        lock (_sampleGate)
        {
            if (_lastSample is { } last && now > last.Ticks)
                cpu = Math.Clamp((sample.CpuTime - last.CpuTime).TotalMilliseconds / (now - last.Ticks)
                    / Environment.ProcessorCount * 100, 0, 100);
            _lastSample = (sample.CpuTime, now);
        }

        return new EngineUsage(sample.MemoryBytes, cpu);
    }

    /// <summary>
    /// 退出码，未退出为 -1
    /// </summary>
    public int ExitCode => _exitCode;

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

    /// <summary>
    /// 启动并等到就绪探针返回 200
    /// </summary>
    /// <param name="launch">怎么起</param>
    /// <param name="onProgress">加载进度（按日志行数估算）</param>
    /// <param name="lifetimeToken">取消即结束进程</param>
    /// <returns>已就绪的服务</returns>
    /// <exception cref="LocalServerException">进程提前退出或超时</exception>
    public static async Task<LocalServerProcess> StartAsync(
        LocalServerLaunch launch,
        Action<float>? onProgress,
        CancellationToken lifetimeToken)
    {
        if (!File.Exists(launch.ExecutablePath))
            throw new LocalServerException($"{launch.ServerName} executable not found: {launch.ExecutablePath}");

        LocalServerProcess server = new(launch, FindFreePort());
        CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        IReadOnlyList<string> args = launch.BuildArguments(server.Port);
        Log.Debug($"Start {launch.ServerName}: {string.Join(' ', args)}");

        server.Completion = Task.Run(() => server.RunAsync(args, onProgress, lifetime.Token), CancellationToken.None);

        try
        {
            await server.WaitUntilReadyAsync(lifetime.Token).ConfigureAwait(false);
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

    private async Task RunAsync(IReadOnlyList<string> args, Action<float>? onProgress, CancellationToken token)
    {
        int processId = 0;
        int lineCount = 0;
        try
        {
            await foreach (CommandEvent cmdEvent in ChildProcessBinding.Wrap(_launch.ExecutablePath, args)
                               .WithEnvironmentVariables(
                                   _launch.Environment?.ToDictionary(x => x.Key, x => (string?)x.Value) ??
                                   new Dictionary<string, string?>())
                               .WithValidation(CommandResultValidation.None)
                               .ListenAsync(token)
                               .ConfigureAwait(false))
            {
                switch (cmdEvent)
                {
                    case StartedCommandEvent started:
                        processId = started.ProcessId;
                        Volatile.Write(ref _launcherId, processId);
                        ChildProcessBinding.OnStarted(processId);
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
                            Log.Warning($"{_launch.ServerName} exited with code {exited.ExitCode}. {LogTail}");
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
            Log.Error($"{_launch.ServerName} failed: {e.Message}");
        }
        finally
        {
            if (processId != 0) ProcessHelper.Untrack(processId);
        }
    }

    private void OnLog(string line, int lineCount, Action<float>? onProgress)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Volatile.Write(ref _lastActivity, Environment.TickCount64);
        Log.Debug(line);
        lock (_logGate)
        {
            _logTail.Enqueue(line);
            if (_logTail.Count > LogTailLines) _logTail.Dequeue();
        }

        // 每 16 行报一次，封顶 0.95，真正的 1 留给就绪
        if (onProgress != null && lineCount % 16 == 0)
            onProgress(Math.Min(0.95f, lineCount / _launch.ExpectedLogLines));
    }

    private async Task WaitUntilReadyAsync(CancellationToken token)
    {
        TimeSpan timeout = _launch.ReadyTimeout;
        Volatile.Write(ref _lastActivity, Environment.TickCount64);
        Uri healthUri = new(BaseUri, _launch.HealthPath);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (Completion.IsCompleted)
                throw new LocalServerException(
                    $"{_launch.ServerName} exited before it was ready (exit code {_exitCode}).", LogTail);
            if (Environment.TickCount64 - Volatile.Read(ref _lastActivity) > (long)timeout.TotalMilliseconds)
                throw new LocalServerException(
                    $"{_launch.ServerName} made no progress for {timeout.TotalSeconds:0}s and was not ready.", LogTail);

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
