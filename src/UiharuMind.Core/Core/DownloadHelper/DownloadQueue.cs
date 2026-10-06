using System.ComponentModel;
using System.Runtime.CompilerServices;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.DownloadHelper;

/// <summary>
/// 下载项状态
/// </summary>
public enum EDownloadJobState
{
    Queued,
    Running,
    Paused,
    Completed,
    Failed
}

/// <summary>
/// 队列里的一个下载项（一个文件）。看 <see cref="State"/> 知道结果；属性变化可能在后台线程触发
/// </summary>
public sealed class DownloadJob : INotifyPropertyChanged
{
    private EDownloadJobState _state = EDownloadJobState.Queued;
    private DownloadProgress _progress;
    private Exception? _error;

    internal DownloadJob(string name, DownloadRequest request, Func<CancellationToken, Task>? onDownloaded)
    {
        Name = name;
        Request = request;
        OnDownloaded = onDownloaded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 显示名
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 下载请求
    /// </summary>
    public DownloadRequest Request { get; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public EDownloadJobState State
    {
        get => _state;
        internal set => Set(ref _state, value);
    }

    /// <summary>
    /// 最近一次进度
    /// </summary>
    public DownloadProgress Progress
    {
        get => _progress;
        internal set => Set(ref _progress, value);
    }

    /// <summary>
    /// 失败原因
    /// </summary>
    public Exception? Error
    {
        get => _error;
        internal set => Set(ref _error, value);
    }

    internal Func<CancellationToken, Task>? OnDownloaded { get; }
    internal CancellationTokenSource? Cancellation { get; set; }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// 全局下载队列：一次只跑一个下载项（项内可分块并发），引擎包与模型共用
/// </summary>
public sealed class DownloadQueue(FileDownloader downloader)
{
    private readonly List<DownloadJob> _jobs = [];
    private readonly Lock _gate = new();
    private bool _isPumping;

    /// <summary>
    /// 全局队列
    /// </summary>
    public static DownloadQueue Shared { get; } = new(FileDownloader.Shared);

    /// <summary>
    /// 下载项增减时触发（可能在后台线程）
    /// </summary>
    public event Action? JobsChanged;

    /// <summary>
    /// 当前所有下载项（快照）
    /// </summary>
    public IReadOnlyList<DownloadJob> Jobs
    {
        get
        {
            lock (_gate) return _jobs.ToList();
        }
    }

    /// <summary>
    /// 排队下载；同一目标文件已在队列里就返回那一项（暂停的会被恢复）
    /// </summary>
    /// <param name="name">显示名</param>
    /// <param name="request">下载请求</param>
    /// <param name="onDownloaded">文件落盘后的处理（解压、写清单…），算在这一项里</param>
    /// <returns>下载项</returns>
    public DownloadJob Enqueue(string name, DownloadRequest request, Func<CancellationToken, Task>? onDownloaded = null)
    {
        DownloadJob job;
        lock (_gate)
        {
            DownloadJob? existing = _jobs.FirstOrDefault(x =>
                x.State is not (EDownloadJobState.Completed or EDownloadJobState.Failed) &&
                string.Equals(x.Request.DestinationPath, request.DestinationPath, StringComparison.Ordinal));
            if (existing != null)
            {
                if (existing.State == EDownloadJobState.Paused) existing.State = EDownloadJobState.Queued;
                job = existing;
            }
            else
            {
                _jobs.RemoveAll(x => string.Equals(x.Request.DestinationPath, request.DestinationPath,
                    StringComparison.Ordinal));
                job = new DownloadJob(name, request, onDownloaded);
                _jobs.Add(job);
            }
        }

        JobsChanged?.Invoke();
        Pump();
        return job;
    }

    /// <summary>
    /// 暂停：停下当前传输，已下部分保留，恢复时接着下
    /// </summary>
    /// <param name="job">下载项</param>
    public void Pause(DownloadJob job)
    {
        lock (_gate)
        {
            if (job.State is not (EDownloadJobState.Queued or EDownloadJobState.Running)) return;
            job.State = EDownloadJobState.Paused;
            job.Cancellation?.Cancel();
        }
    }

    /// <summary>
    /// 恢复暂停或失败的下载项
    /// </summary>
    /// <param name="job">下载项</param>
    public void Resume(DownloadJob job)
    {
        lock (_gate)
        {
            if (job.State is not (EDownloadJobState.Paused or EDownloadJobState.Failed)) return;
            job.Error = null;
            job.State = EDownloadJobState.Queued;
        }

        Pump();
    }

    /// <summary>
    /// 取消并删掉已下部分
    /// </summary>
    /// <param name="job">下载项</param>
    public void Cancel(DownloadJob job)
    {
        bool wasRunning;
        lock (_gate)
        {
            if (!_jobs.Remove(job)) return;
            wasRunning = job.State == EDownloadJobState.Running;
            job.State = EDownloadJobState.Paused;
            job.Cancellation?.Cancel();
        }

        // 运行中的由传输循环在退出后清理，避免和正在写的文件抢
        if (!wasRunning) FileDownloader.DeletePartial(job.Request.DestinationPath);
        JobsChanged?.Invoke();
    }

    private void Pump()
    {
        lock (_gate)
        {
            if (_isPumping) return;
            _isPumping = true;
        }

        _ = Task.Run(PumpAsync);
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            DownloadJob? job;
            lock (_gate)
            {
                job = _jobs.FirstOrDefault(x => x.State == EDownloadJobState.Queued);
                if (job == null)
                {
                    _isPumping = false;
                    return;
                }

                job.State = EDownloadJobState.Running;
                job.Cancellation = new CancellationTokenSource();
            }

            await RunAsync(job).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(DownloadJob job)
    {
        CancellationToken token = job.Cancellation!.Token;
        try
        {
            await downloader.DownloadAsync(job.Request, new InlineProgress(p => job.Progress = p), token)
                .ConfigureAwait(false);
            if (job.OnDownloaded != null) await job.OnDownloaded(token).ConfigureAwait(false);
            job.State = EDownloadJobState.Completed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 暂停保留进度；被取消的已移出队列，这里删残留
            bool removed;
            lock (_gate) removed = !_jobs.Contains(job);
            if (removed) FileDownloader.DeletePartial(job.Request.DestinationPath);
        }
        catch (Exception e)
        {
            Log.Warning($"Download failed: {job.Name}, {e.Message}");
            job.Error = e;
            job.State = EDownloadJobState.Failed;
        }
        finally
        {
            job.Cancellation?.Dispose();
            job.Cancellation = null;
        }
    }

    // Progress<T> 会抓同步上下文投递，下载线程上直接回调即可，界面层自己切线程
    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
