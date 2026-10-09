using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Engines;

/// <summary>
/// 本地引擎服务的运行状态
/// </summary>
public enum EEngineRunState
{
    Stopped,
    Starting,
    Running,

    /// <summary>
    /// 连着起不来或崩溃，冷却中：这段时间直接报最近的错，不再反复拉
    /// </summary>
    Failed
}

/// <summary>
/// 引擎进程此刻的实际占用
/// </summary>
/// <param name="MemoryBytes">内存（字节）</param>
/// <param name="CpuPercent">占全部核心的百分比（0~100）；头一回取样还算不出为 null</param>
/// <param name="GpuMemoryBytes">独立显卡上的显存（不在 <paramref name="MemoryBytes"/> 里）；统一内存或读不到为 null</param>
public sealed record EngineUsage(long MemoryBytes, double? CpuPercent, long? GpuMemoryBytes = null);

/// <summary>
/// 引擎进程的运行面：在不在跑、最近为什么起不来、停掉释放内存。界面的状态条只认这一层
/// </summary>
public interface IEngineRuntime
{
    /// <summary>
    /// 运行状态
    /// </summary>
    EEngineRunState State { get; }

    /// <summary>
    /// 最近一次起不来或崩溃的原因；之后成功起来就清掉
    /// </summary>
    LocalServerException? LastError { get; }

    /// <summary>
    /// 启动中的加载进度（0~1，按日志行数估）；不在启动为 0
    /// </summary>
    float LoadProgress { get; }

    /// <summary>
    /// 取一次在跑进程的实际占用（CPU 按与上次取样之间算）；没在跑为 null
    /// </summary>
    EngineUsage? SampleUsage();

    /// <summary>
    /// 状态变了（可能在后台线程）
    /// </summary>
    event Action? StateChanged;

    /// <summary>
    /// 加载进度变了（后台线程）
    /// </summary>
    event Action? LoadProgressChanged;

    /// <summary>
    /// 停掉，释放内存；下次使用时按当时的启动描述再拉起
    /// </summary>
    void Stop();
}

/// <summary>
/// 一个本地引擎服务的进程宿主：用到才拉起，并发请求共用同一次启动，退出了下次用自动重启。
/// 进程寿命只归宿主，请求取消只放弃等待；被 <see cref="Stop"/> 打断的等待改等新的那次，不会收到取消。
/// 连着起不来（或起来就崩）几次就冷却一阵，期间直接报最近的错。可选闲置自动停（<see cref="BeginUse"/> 计用量）。
/// 调用方带上启动指纹（模型、版本、启动设置），和在跑的不一样就换进程；换进程先等旧的退干净再起，免得两份权重同时占内存。
/// 启动描述估了占用的，拉起前按此刻可用内存检查，危险就不起（<see cref="EngineMemoryException"/>）。
/// 启动描述在锁外取，取描述的回调可以放心去拿调用方自己的锁
/// </summary>
public sealed class LocalServerHost : IEngineRuntime, IDisposable
{
    /// <summary>
    /// 连着失败几次进冷却
    /// </summary>
    public const int FailuresBeforeCooldown = 3;

    private static readonly TimeSpan PreviousExitWait = TimeSpan.FromSeconds(15);

    private readonly Func<LocalServerLaunch> _createLaunch;
    private readonly Func<TimeSpan?> _idleTimeout;
    private readonly TimeSpan _cooldown;
    private readonly TimeProvider _time;
    private readonly Func<RuntimeDeviceInfo> _deviceInfo;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _disposed = new();

    private Task<LocalServerProcess>? _starting; //最近一次启动，成功后即为在跑的服务
    private string? _startingKey; //最近一次启动的指纹
    private Task _previousExit = Task.CompletedTask; //上一个被停掉的进程退出
    private float _loadProgress;
    private CancellationTokenSource? _serverLifetime;
    private int _generation; //每次拉起或停掉加一，等待方据此知道自己等的那次被换掉了
    private int _failures;
    private DateTimeOffset _coolUntil;
    private LocalServerException? _lastError;
    private int _activeUses;
    private ITimer? _idleTimer;

    /// <param name="createLaunch">每次启动时取当时的启动描述（换了模型、版本或设置，下次启动生效）</param>
    /// <param name="idleTimeout">闲置多久自动停；null 不停（由服务自己卸模型的引擎）</param>
    /// <param name="cooldown">连着失败后冷却多久；null 为 30 秒</param>
    /// <param name="time">时钟；null 用系统时钟</param>
    /// <param name="deviceInfo">取此刻的内存；null 用本机的</param>
    public LocalServerHost(Func<LocalServerLaunch> createLaunch, TimeSpan? idleTimeout = null,
        TimeSpan? cooldown = null, TimeProvider? time = null, Func<RuntimeDeviceInfo>? deviceInfo = null)
        : this(createLaunch, () => idleTimeout, cooldown, time, deviceInfo)
    {
    }

    /// <param name="createLaunch">每次启动时取当时的启动描述</param>
    /// <param name="idleTimeout">每次用完时取闲置多久自动停（随设置变）；返回 null 不停</param>
    /// <param name="cooldown">连着失败后冷却多久；null 为 30 秒</param>
    /// <param name="time">时钟；null 用系统时钟</param>
    /// <param name="deviceInfo">取此刻的内存；null 用本机的</param>
    public LocalServerHost(Func<LocalServerLaunch> createLaunch, Func<TimeSpan?> idleTimeout,
        TimeSpan? cooldown = null, TimeProvider? time = null, Func<RuntimeDeviceInfo>? deviceInfo = null)
    {
        _createLaunch = createLaunch;
        _idleTimeout = idleTimeout;
        _cooldown = cooldown ?? TimeSpan.FromSeconds(30);
        _time = time ?? TimeProvider.System;
        _deviceInfo = deviceInfo ?? (() => RuntimeDeviceInfoProvider.Capture(true));
    }

    public event Action? StateChanged;
    public event Action? LoadProgressChanged;

    public float LoadProgress => Volatile.Read(ref _loadProgress);

    public EEngineRunState State
    {
        get
        {
            lock (_gate)
            {
                if (_starting is { IsCompleted: false }) return EEngineRunState.Starting;
                if (IsAlive(_starting)) return EEngineRunState.Running;
                return _time.GetUtcNow() < _coolUntil ? EEngineRunState.Failed : EEngineRunState.Stopped;
            }
        }
    }

    public LocalServerException? LastError
    {
        get
        {
            lock (_gate) return _lastError;
        }
    }

    /// <summary>
    /// 在跑的服务的根地址；没在跑（或还在启动）为 null。只看不拉起
    /// </summary>
    public Uri? RunningBaseUri
    {
        get
        {
            lock (_gate)
            {
                return _starting is { IsCompletedSuccessfully: true } started && !started.Result.Completion.IsCompleted
                    ? started.Result.BaseUri
                    : null;
            }
        }
    }

    public EngineUsage? SampleUsage()
    {
        LocalServerProcess? server;
        lock (_gate) server = _starting is { IsCompletedSuccessfully: true } started ? started.Result : null;
        return server?.SampleUsage();
    }

    /// <summary>
    /// 正在启动（等就绪中）
    /// </summary>
    public bool IsStarting => State == EEngineRunState.Starting;

    /// <summary>
    /// 服务根地址；没在跑就拉起
    /// </summary>
    /// <param name="cancellationToken">只取消这次等待</param>
    /// <returns>服务根地址</returns>
    /// <exception cref="LocalServerException">起不来，或正在冷却</exception>
    public Task<Uri> GetBaseUriAsync(CancellationToken cancellationToken) =>
        GetBaseUriAsync(null, false, cancellationToken);

    /// <summary>
    /// 服务根地址；没在跑就拉起，在跑的指纹不一样就换进程
    /// </summary>
    /// <param name="launchKey">要的启动指纹；null 为在跑的都行</param>
    /// <param name="allowMemoryRisk">内存危险也照样起（问过用户的）</param>
    /// <param name="cancellationToken">只取消这次等待</param>
    /// <returns>服务根地址</returns>
    /// <exception cref="LocalServerException">起不来，或正在冷却</exception>
    /// <exception cref="EngineMemoryException">此刻内存不够，没起</exception>
    public async Task<Uri> GetBaseUriAsync(string? launchKey, bool allowMemoryRisk,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            (Task<LocalServerProcess> starting, int generation) = EnsureStarting(launchKey, allowMemoryRisk);
            try
            {
                LocalServerProcess server = await starting.WaitAsync(cancellationToken).ConfigureAwait(false);
                return server.BaseUri;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested && IsReplaced(generation) &&
                                      e is OperationCanceledException or LocalServerException)
            {
                // 等的那次被停掉换了新的：改等新的
            }
        }
    }

    /// <summary>
    /// 开始用一次（请求开始时调，用完释放）：闲置计时从最后一次用完算起，用着的时候不会被闲置停掉
    /// </summary>
    /// <returns>释放即用完</returns>
    public IDisposable BeginUse()
    {
        lock (_gate)
        {
            _activeUses++;
            _idleTimer?.Dispose();
            _idleTimer = null;
        }

        return new Use(this);
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
            _failures = 0; //换了设置、模型或版本，之前的失败不算数
            _coolUntil = default;
        }

        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed.IsCancellationRequested) return;
            _disposed.Cancel();
            _idleTimer?.Dispose();
        }
    }

    private (Task<LocalServerProcess> Starting, int Generation) EnsureStarting(string? launchKey,
        bool allowMemoryRisk)
    {
        TaskCompletionSource<LocalServerProcess> pending;
        CancellationToken lifetime;
        Task previousExit;
        int generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
            bool changed = launchKey != null && launchKey != _startingKey;
            if (IsAlive(_starting) && !changed) return (_starting!, _generation);
            if (changed)
            {
                // 换了模型、版本或设置：之前的失败不算数
                _failures = 0;
                _coolUntil = default;
            }
            else if (_time.GetUtcNow() < _coolUntil && _lastError != null)
                throw new LocalServerException(_lastError.Message, _lastError.LogTail);

            StopCore();
            _serverLifetime = CancellationTokenSource.CreateLinkedTokenSource(_disposed.Token);
            lifetime = _serverLifetime.Token;
            pending = new TaskCompletionSource<LocalServerProcess>(TaskCreationOptions.RunContinuationsAsynchronously);
            _starting = pending.Task;
            _startingKey = launchKey ?? _startingKey;
            previousExit = _previousExit;
            generation = _generation;
        }

        StateChanged?.Invoke();
        _ = Task.Run(() => StartAsync(pending, lifetime, generation, allowMemoryRisk, previousExit),
            CancellationToken.None);
        return (pending.Task, generation);
    }

    private async Task StartAsync(TaskCompletionSource<LocalServerProcess> pending, CancellationToken lifetime,
        int generation, bool allowMemoryRisk, Task previousExit)
    {
        LocalServerLaunch launch;
        try
        {
            launch = _createLaunch();
            // 旧进程还占着内存：等它退干净再查、再起
            await WaitPreviousExitAsync(previousExit, lifetime).ConfigureAwait(false);
            if (!allowMemoryRisk) CheckMemory(launch);
        }
        catch (OperationCanceledException e) when (lifetime.IsCancellationRequested)
        {
            pending.TrySetCanceled(e.CancellationToken);
            StateChanged?.Invoke();
            return;
        }
        catch (Exception e)
        {
            // 没装引擎、没有模型、内存不够之类：不是进程的错，原样交给调用方，也不计失败
            pending.TrySetException(e);
            StateChanged?.Invoke();
            return;
        }

        try
        {
            SetLoadProgress(0f);
            LocalServerProcess server = await LocalServerProcess.StartAsync(launch, SetLoadProgress, lifetime)
                .ConfigureAwait(false);
            lock (_gate)
            {
                _failures = 0;
                _lastError = null;
            }

            _ = server.Completion.ContinueWith(_ => OnExited(server, generation), TaskScheduler.Default);
            pending.TrySetResult(server);
        }
        catch (OperationCanceledException e) when (lifetime.IsCancellationRequested)
        {
            pending.TrySetCanceled(e.CancellationToken);
        }
        catch (Exception e)
        {
            LocalServerException error = e as LocalServerException ?? new LocalServerException(e.Message);
            RecordFailure(error, generation);
            pending.TrySetException(error);
        }

        SetLoadProgress(0f);
        StateChanged?.Invoke();
    }

    private static async Task WaitPreviousExitAsync(Task previousExit, CancellationToken lifetime)
    {
        if (previousExit.IsCompleted) return;
        try
        {
            await previousExit.WaitAsync(PreviousExitWait, lifetime).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Log.Warning("Previous local server did not exit in time; starting the new one anyway.");
        }
    }

    // 只拦「危险」：「偏紧」照起，内存信息拿不到也照起
    private void CheckMemory(LocalServerLaunch launch)
    {
        if (launch.EstimatedMemoryBytes <= 0) return;
        RuntimeDeviceInfo device = _deviceInfo();
        if (RuntimeLoadRiskEvaluator.ClassifyBeforeLoad(launch.EstimatedMemoryBytes, device) ==
            RuntimeLoadRiskLevel.Danger)
            throw new EngineMemoryException(launch.ServerName,
                new EngineMemoryShortfall(launch.EstimatedMemoryBytes, device.AvailableMemoryBytes));
    }

    private void SetLoadProgress(float progress)
    {
        if (Interlocked.Exchange(ref _loadProgress, progress) != progress) LoadProgressChanged?.Invoke();
    }

    // 没被要求停却退出了：算一次失败
    private void OnExited(LocalServerProcess server, int generation)
    {
        lock (_gate)
        {
            if (generation != _generation || _disposed.IsCancellationRequested) return;
        }

        RecordFailure(new LocalServerException($"Server exited unexpectedly (exit code {server.ExitCode}).",
            server.LogTail), generation);
        StateChanged?.Invoke();
    }

    private void RecordFailure(LocalServerException error, int generation)
    {
        lock (_gate)
        {
            if (generation != _generation) return;
            _lastError = error;
            if (++_failures < FailuresBeforeCooldown) return;
            _coolUntil = _time.GetUtcNow() + _cooldown;
            _failures = 0;
        }

        Log.Warning($"Local server keeps failing, cooling down for {_cooldown.TotalSeconds:0}s: {error.Message}");
    }

    private void EndUse()
    {
        lock (_gate)
        {
            if (--_activeUses > 0 || _disposed.IsCancellationRequested) return;
            _idleTimer?.Dispose();
            _idleTimer = null;
            if (_idleTimeout() is not { } idle) return;
            int generation = _generation;
            _idleTimer = _time.CreateTimer(_ => StopIfIdle(generation), null, idle, Timeout.InfiniteTimeSpan);
        }
    }

    private void StopIfIdle(int generation)
    {
        lock (_gate)
        {
            if (_activeUses > 0 || generation != _generation || _starting == null) return;
            StopCore();
        }

        Log.Debug("Local server stopped after being idle.");
        StateChanged?.Invoke();
    }

    // 调用方持锁
    private void StopCore()
    {
        if (_starting is { IsCompletedSuccessfully: true } started) _previousExit = started.Result.Completion;
        _serverLifetime?.Cancel();
        _serverLifetime?.Dispose();
        _serverLifetime = null;
        _starting = null; //旧进程退出是异步的，不等它，下次直接起新的
        _generation++;
    }

    private bool IsReplaced(int generation)
    {
        lock (_gate) return generation != _generation;
    }

    // 还在启动中，或已起来且没退出
    private static bool IsAlive(Task<LocalServerProcess>? starting)
    {
        if (starting == null || starting.IsFaulted || starting.IsCanceled) return false;
        return !starting.IsCompletedSuccessfully || !starting.Result.Completion.IsCompleted;
    }

    private sealed class Use(LocalServerHost host) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) host.EndUse();
        }
    }
}
