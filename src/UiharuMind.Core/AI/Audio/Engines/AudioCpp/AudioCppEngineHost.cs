using UiharuMind.Core.AI.Engines;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// audiocpp_server 的进程宿主：首次使用时拉起、退出后再用自动重启、并发请求共用同一次启动。
/// 进程寿命只归宿主，请求取消只放弃等待，不会杀掉别人也在用的服务；模型闲置卸载交给 server 的 idle_unload_ms
/// </summary>
internal sealed class AudioCppEngineHost : IDisposable
{
    private readonly Func<LocalServerLaunch> _createLaunch;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _disposed = new();

    private Task<LocalServerProcess>? _starting; //最近一次启动，成功后即为在跑的服务
    private CancellationTokenSource? _serverLifetime;

    /// <param name="createLaunch">每次启动时取当时的启动描述（换了模型或引擎，重启后生效）</param>
    public AudioCppEngineHost(Func<LocalServerLaunch> createLaunch)
    {
        _createLaunch = createLaunch;
    }

    /// <summary>
    /// 服务根地址；没在跑就拉起
    /// </summary>
    /// <param name="cancellationToken">只取消这次等待</param>
    /// <returns>服务根地址</returns>
    /// <exception cref="LocalServerException">起不来</exception>
    public async Task<Uri> GetBaseUriAsync(CancellationToken cancellationToken)
    {
        Task<LocalServerProcess> starting;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
            if (!IsAlive(_starting))
            {
                _serverLifetime?.Cancel();
                _serverLifetime?.Dispose();
                _serverLifetime = CancellationTokenSource.CreateLinkedTokenSource(_disposed.Token);
                _starting = LocalServerProcess.StartAsync(_createLaunch(), null, _serverLifetime.Token);
            }

            starting = _starting!;
        }

        LocalServerProcess server = await starting.WaitAsync(cancellationToken).ConfigureAwait(false);
        return server.BaseUri;
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

    /// <summary>
    /// 正在启动（等就绪中）
    /// </summary>
    public bool IsStarting
    {
        get
        {
            lock (_gate) return _starting is { IsCompleted: false };
        }
    }

    /// <summary>
    /// 停掉当前进程；下次使用会按新的启动描述重新拉起（换了模型、引擎时用）
    /// </summary>
    public void Restart()
    {
        lock (_gate)
        {
            _serverLifetime?.Cancel();
            _starting = null; //旧进程退出是异步的，不等它，下次直接起新的
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed.IsCancellationRequested) return;
            _disposed.Cancel();
        }
    }

    // 还在启动中，或已起来且没退出
    private static bool IsAlive(Task<LocalServerProcess>? starting)
    {
        if (starting == null || starting.IsFaulted || starting.IsCanceled) return false;
        return !starting.IsCompletedSuccessfully || !starting.Result.Completion.IsCompleted;
    }
}
