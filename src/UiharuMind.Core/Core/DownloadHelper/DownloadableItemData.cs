/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.DownloadHelper;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils.Tools;

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 下载项的界面数据：把全局下载队列里的一项映射成可绑定的进度、状态
/// </summary>
public class DownloadableItemData : INotifyPropertyChanged, IDisposable
{
    private readonly IDownloadable _target;

    private DownloadJob? _job;
    private double _downloadProgress;
    private string? _downloadInfo;
    private string? _errorMessage;
    private string? _totalSizeInfo;
    private bool _isLoading = false;
    // private bool _isDownloaded = false;

    //对 IDownloadable 基本信息的封装
    public IDownloadable Target => _target;
    public string Name => _target.Name;
    public string DownloadUrl => _target.DownloadUrl;

    /// <summary>
    /// 下载完成回调
    /// </summary>
    private Action<DownloadableItemData>? _onDownloadCompleted;

    /// <summary>
    /// 文件总大小，提前获取的值
    /// </summary>
    public string? TotalSizeInfo
    {
        get => _totalSizeInfo;
        set
        {
            _totalSizeInfo = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 下载信息
    /// </summary>
    public string? DownloadInfo
    {
        get => _downloadInfo;
        set
        {
            _downloadInfo = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 下载进度百分比
    /// 0~100
    /// </summary>
    public double DownloadProgress
    {
        get => _downloadProgress;
        private set
        {
            _downloadProgress = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 是否处于下载中
    /// </summary>
    public bool IsDownloading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 是否已下载完成
    /// </summary>
    public bool IsDownloaded
    {
        get => _target.IsDownloaded;
        set
        {
            _target.IsDownloaded = value;
            OnPropertyChanged();
        }
    }

    public bool IsNotAllowDelete => _target.IsNotAllowDelete;

    // /// <summary>
    // /// 是否正在下载
    // /// </summary>
    // public bool IsDownloading => _downloadService?.Status == DownloadStatus.Running;

    /// <summary>
    /// 下载完成的文件路径
    /// </summary>
    public string DownloadFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 是否需要显示下载信息
    /// </summary>
    public bool IsNeedDownloadInfo { get; set; } = true;

    public DownloadableItemData(IDownloadable target, bool initDownloadSize = false)
    {
        _target = target;
        DownloadFilePath = ResolveDownloadFilePath();
        if (initDownloadSize) InitFileSize();
    }

    /// <summary>
    /// 排进全局下载队列；已在队列里（含暂停）则接着下
    /// </summary>
    /// <param name="onDownloadFileCompleted">下载完成回调</param>
    public void StartDownload(Action<DownloadableItemData>? onDownloadFileCompleted)
    {
        _onDownloadCompleted = onDownloadFileCompleted;
        DownloadInfo = "Preparing to download...";
        ErrorMessage = null;
        DownloadProgress = 0;
        IsDownloaded = false;
        DownloadFilePath = ResolveDownloadFilePath();
        IsDownloading = true;

        if (_job != null) _job.PropertyChanged -= OnJobChanged;
        _job = DownloadQueue.Shared.Enqueue(Name, new DownloadRequest(
            new Uri(ModelSources.ApplyGitHubProxy(DownloadUrl)), DownloadFilePath, _target.SegmentCount,
            _target.Sha256));
        _job.PropertyChanged += OnJobChanged;
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        DownloadJob job = (DownloadJob)sender!;
        switch (e.PropertyName)
        {
            case nameof(DownloadJob.Progress):
                UpdateDownloadProgress(job.Progress);
                break;
            case nameof(DownloadJob.State) when job.State == EDownloadJobState.Completed:
                IsDownloading = false;
                IsDownloaded = true;
                _onDownloadCompleted?.Invoke(this);
                break;
            case nameof(DownloadJob.State) when job.State == EDownloadJobState.Failed:
                IsDownloading = false;
                ErrorMessage = job.Error?.Message;
                break;
            case nameof(DownloadJob.State) when job.State == EDownloadJobState.Paused:
                IsDownloading = false;
                break;
        }
    }

    private void UpdateDownloadProgress(DownloadProgress progress)
    {
        if (IsDownloaded) return;

        if (progress.TotalBytes is > 0 and var total) DownloadProgress = progress.ReceivedBytes * 100.0 / total;
        if (!IsNeedDownloadInfo) return;
        DownloadInfo =
            $"{SimpleStringHelper.FormatBytes(progress.ReceivedBytes)} / {SimpleStringHelper.FormatBytes(progress.TotalBytes ?? 0)} ({SimpleStringHelper.FormatBytesWithSpeed(progress.BytesPerSecond)})";
    }

    /// <summary>
    /// 通过请求头信息获取文件大小
    /// 当然，如果已经下载，则直接获取文件大小
    /// </summary>
    public async void InitFileSize()
    {
        RefreshDownloadedState();
        if (IsDownloaded)
        {
            string? downloadedSizePath = GetDownloadedSizePath();
            if (string.IsNullOrWhiteSpace(downloadedSizePath))
            {
                TotalSizeInfo = string.Empty;
                return;
            }

            long size = await SimpleFileHelper.GetFileOrDirectorySizeAsync(downloadedSizePath);
            TotalSizeInfo = size < 0 ? string.Empty : SimpleStringHelper.FormatBytes(size);
            return;
        }

        try
        {
            using HttpClient client = new HttpClient();
            // 发送HEAD请求
            using HttpResponseMessage response =
                await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, DownloadUrl));
            if (response.IsSuccessStatusCode)
            {
                // 检查Content-Length是否存在
                if (response.Content.Headers.ContentLength.HasValue)
                {
                    long fileSize = response.Content.Headers.ContentLength.Value;
                    TotalSizeInfo = SimpleStringHelper.FormatBytes(fileSize);
                }
                else
                {
                    Log.Warning($"无法获取文件 {DownloadUrl} 大小，因为服务器没有返回Content-Length头信息。");
                }
            }
            else
            {
                Log.Warning($"请求 {DownloadUrl} 失败，状态码: {response.StatusCode}");
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
            ErrorMessage = e.Message;
            TotalSizeInfo = "Error";
        }
    }

    private string ResolveDownloadFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_target.DownloadFileName)) return _target.DownloadFileName;
        if (!string.IsNullOrWhiteSpace(_target.DownloadDirectory))
        {
            string fileName = Path.GetFileName(_target.DownloadUrl);
            return string.IsNullOrWhiteSpace(fileName)
                ? _target.DownloadDirectory
                : Path.Combine(_target.DownloadDirectory, fileName);
        }

        // 下载至缓存目录
        string cachedFileName = Path.GetFileName(_target.DownloadUrl);
        return Path.Combine(Path.GetTempPath(),
            string.IsNullOrWhiteSpace(cachedFileName) ? _target.Name : cachedFileName);
    }

    private string? GetDownloadedSizePath()
    {
        RefreshDownloadedState();
        if (_target is IInstalledDownloadable installedDownloadable &&
            !string.IsNullOrWhiteSpace(installedDownloadable.InstalledPath) &&
            (File.Exists(installedDownloadable.InstalledPath) || Directory.Exists(installedDownloadable.InstalledPath)))
        {
            return installedDownloadable.InstalledPath;
        }

        if (File.Exists(DownloadFilePath) || Directory.Exists(DownloadFilePath)) return DownloadFilePath;
        if (!string.IsNullOrWhiteSpace(_target.DownloadDirectory) &&
            (File.Exists(_target.DownloadDirectory) || Directory.Exists(_target.DownloadDirectory)))
        {
            return _target.DownloadDirectory;
        }

        if (!string.IsNullOrWhiteSpace(_target.DownloadFileName) &&
            (File.Exists(_target.DownloadFileName) || Directory.Exists(_target.DownloadFileName)))
        {
            return _target.DownloadFileName;
        }

        return null;
    }

    public void RefreshDownloadedState()
    {
        if (_target is ManagedVersionPackage package)
        {
            package.RefreshState();
            OnPropertyChanged(nameof(IsDownloaded));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool _disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 与旧实现一致：列表清掉即停下；已下部分保留，再点下载接着下
                if (_job != null)
                {
                    _job.PropertyChanged -= OnJobChanged;
                    DownloadQueue.Shared.Pause(_job);
                }
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~DownloadableItemData()
    {
        Dispose(false);
    }
}
