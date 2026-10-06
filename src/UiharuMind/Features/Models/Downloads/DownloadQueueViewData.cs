using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 下载队列的界面镜像：只列未完成的项。队列的事件来自后台线程，这里统一切回界面线程
/// </summary>
public partial class DownloadQueueViewData : ObservableObject
{
    private readonly DownloadQueue _queue;
    private readonly Action<Action> _post; //切回界面线程
    private int _isSyncPending; //同一拍里多次变化只同步一次（队列事件来自多个线程）

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActive))]
    private int _activeCount;
    [ObservableProperty] private bool _isExpanded = true;

    /// <param name="queue">下载队列</param>
    /// <param name="post">切回界面线程的方式，默认投递到 Avalonia 调度线程（纯逻辑测试传同步执行）</param>
    public DownloadQueueViewData(DownloadQueue queue, Action<Action>? post = null)
    {
        _queue = queue;
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        _queue.JobsChanged += ScheduleSync;
        Sync();
    }

    /// <summary>
    /// 未完成的下载项
    /// </summary>
    public ObservableCollection<DownloadJobItemData> Items { get; } = [];

    /// <summary>
    /// 有排队或下载中的项（页签头的数字徽标）
    /// </summary>
    public bool HasActive => ActiveCount > 0;

    /// <summary>
    /// 有下载项
    /// </summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>
    /// 标题行的概况：进行中几项、共几项
    /// </summary>
    public string Summary => Loc.Text(LangKey.ModelDownloadQueueSummary, ActiveCount, Items.Count);

    /// <summary>
    /// 任一下载项的状态或进度变了（界面线程上触发，行状态靠它刷新）
    /// </summary>
    public event Action? Changed;

    [RelayCommand]
    private void Pause(DownloadJobItemData item) => _queue.Pause(item.Job);

    [RelayCommand]
    private void Resume(DownloadJobItemData item) => _queue.Resume(item.Job);

    [RelayCommand]
    private void Cancel(DownloadJobItemData item) => _queue.Cancel(item.Job);

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    private void ScheduleSync()
    {
        if (Interlocked.Exchange(ref _isSyncPending, 1) == 1) return;
        _post(() =>
        {
            Volatile.Write(ref _isSyncPending, 0);
            Sync();
        });
    }

    private void Sync()
    {
        DownloadJob[] jobs = _queue.Jobs.Where(x => x.State != EDownloadJobState.Completed).ToArray();
        foreach (DownloadJobItemData item in Items.Where(x => !jobs.Contains(x.Job)).ToArray())
        {
            item.Job.PropertyChanged -= OnJobChanged;
            Items.Remove(item);
        }

        foreach (DownloadJob job in jobs.Where(x => Items.All(item => item.Job != x)))
        {
            job.PropertyChanged += OnJobChanged;
            Items.Add(new DownloadJobItemData(job));
        }

        foreach (DownloadJobItemData item in Items) item.Refresh();
        ActiveCount = Items.Count(x => x.Job.State is EDownloadJobState.Queued or EDownloadJobState.Running);
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke();
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e) => ScheduleSync();
}

/// <summary>
/// 下载区的一项
/// </summary>
public partial class DownloadJobItemData(DownloadJob job) : ObservableObject
{
    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private bool _canPause;
    [ObservableProperty] private bool _canResume;

    /// <summary>
    /// 队列里的下载项
    /// </summary>
    public DownloadJob Job { get; } = job;

    /// <summary>
    /// 显示名
    /// </summary>
    public string Name => Job.Name;

    /// <summary>
    /// 按下载项当前状态刷新显示
    /// </summary>
    public void Refresh()
    {
        DownloadProgress progress = Job.Progress;
        Percent = progress.TotalBytes is > 0 and var total ? progress.ReceivedBytes * 100.0 / total : 0;
        string bytes = progress.TotalBytes is > 0
            ? $"{GameUtils.FormatBytes(progress.ReceivedBytes)} / {GameUtils.FormatBytes(progress.TotalBytes.Value)}"
            : "";
        Detail = Job.State switch
        {
            EDownloadJobState.Running => $"{bytes}  {SimpleStringHelper.FormatBytesWithSpeed(progress.BytesPerSecond)}",
            EDownloadJobState.Queued => Loc.Text(LangKey.ModelDownloadStateQueued),
            EDownloadJobState.Paused => $"{Loc.Text(LangKey.ModelDownloadStatePaused)}  {bytes}",
            EDownloadJobState.Failed => Loc.Text(LangKey.ModelDownloadStateFailed, Job.Error?.Message ?? ""),
            _ => ""
        };
        CanPause = Job.State is EDownloadJobState.Queued or EDownloadJobState.Running;
        CanResume = Job.State is EDownloadJobState.Paused or EDownloadJobState.Failed;
    }
}
