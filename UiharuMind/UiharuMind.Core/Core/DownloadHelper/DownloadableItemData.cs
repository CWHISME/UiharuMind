using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using Downloader;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils.Tools;
using DownloadProgressChangedEventArgs = Downloader.DownloadProgressChangedEventArgs;

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 封装 Downloader 下载器的下载项数据，支持直接绑定到 UI 组件上，并提供下载进度的实时更新
/// </summary>
public class DownloadableItemData : INotifyPropertyChanged, IDisposable
{
    private readonly IDownloadable _target;

    private DownloadService? _downloadService;
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

    private ValueBackgroundDelayUpdater<DownloadProgressChangedEventArgs>? _delayUpdater;

    public DownloadableItemData(IDownloadable target, bool initDownloadSize = false)
    {
        _target = target;
        if (initDownloadSize) InitFileSize();
    }

    /// <summary>
    /// 执行下载
    /// 如果不需要显示下载信息，请设置 IsNeedDownloadInfo 为 false
    /// </summary>
    /// <param name="onDownloadFileCompleted"></param>
    /// <param name="configuration"></param>
    public async void StartDownload(Action<DownloadableItemData>? onDownloadFileCompleted,
        DownloadConfiguration? configuration = null)
    {
        _delayUpdater ??=
            new ValueBackgroundDelayUpdater<DownloadProgressChangedEventArgs>(UpdateDownloadProgress, 200);

        _onDownloadCompleted = onDownloadFileCompleted;
        _downloadService = CreateDownloadOpt(true, configuration);
        _downloadService.DownloadProgressChanged += OnDownloadProgressChanged;
        _downloadService.DownloadFileCompleted += OnDownloadFileCompleted;


        DownloadInfo = "Preparing to download...";
        ErrorMessage = null;
        DownloadProgress = 0;
        IsDownloaded = false;

        if (!string.IsNullOrWhiteSpace(_target.DownloadFileName)) DownloadFilePath = _target.DownloadFileName;
        else if (!string.IsNullOrWhiteSpace(_target.DownloadDirectory))
            DownloadFilePath = Path.Combine(_target.DownloadDirectory, Path.GetFileName(_target.DownloadUrl));
        else
        {
            //下载至缓存目录
            DownloadFilePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(_target.DownloadUrl));
        }

        IsDownloading = true;
        await _downloadService.DownloadFileTaskAsync(DownloadUrl, DownloadFilePath).ConfigureAwait(false);
        // await Task.Run(async () =>
        // {
        //     while (true)
        //     {
        //         await Task.Delay(10);
        //         OnDownloadProgressChanged(this, new DownloadProgressChangedEventArgs("1"));
        //     }
        // });
        IsDownloading = false;
    }


    private void OnDownloadProgressChanged(object? sender, DownloadProgressChangedEventArgs e)
    {
        _delayUpdater?.UpdateValue(e);
    }

    private void OnDownloadFileCompleted(object? sender, AsyncCompletedEventArgs e)
    {
        if (e.Error != null)
        {
            Log.Warning(e.Error.Message);
            ErrorMessage = e.Error.Message;
        }
        else
        {
            IsDownloaded = true;
            _onDownloadCompleted?.Invoke(this);
        }
    }

    private void UpdateDownloadProgress(DownloadProgressChangedEventArgs e)
    {
        if (IsDownloaded) return;

        DownloadProgress = e.ProgressPercentage;

        if (!IsNeedDownloadInfo) return;
        DownloadInfo =
            $"{SimpleStringHelper.FormatBytes(e.ReceivedBytesSize)} / {SimpleStringHelper.FormatBytes(e.TotalBytesToReceive)} ({SimpleStringHelper.FormatBytesWithSpeed(e.BytesPerSecondSpeed)})";
    }

    private DownloadService CreateDownloadOpt(bool useDefaultProxy, DownloadConfiguration? configuration = null)
    {
        var downloadOpt = configuration ?? new DownloadConfiguration()
        {
            BufferBlockSize = 10240,
            ParallelDownload = true,
            ParallelCount = Environment.ProcessorCount / 2,
            MaximumMemoryBufferBytes = 1024 * 1024 * 100,
        };

        if (useDefaultProxy)
        {
            // 使用系统的代理设置
            var proxy = WebRequest.DefaultWebProxy;
            if (proxy != null)
            {
                proxy.Credentials = CredentialCache.DefaultCredentials;

                downloadOpt.RequestConfiguration = new RequestConfiguration
                {
                    UseDefaultCredentials = true,
                    Proxy = proxy
                };
            }
        }

        return new DownloadService(downloadOpt);
    }

    /// <summary>
    /// 通过请求头信息获取文件大小
    /// 当然，如果已经下载，则直接获取文件大小
    /// </summary>
    public async void InitFileSize()
    {
        if (IsDownloaded)
        {
            TotalSizeInfo =
                SimpleStringHelper.FormatBytes(
                    await SimpleFileHelper.GetFileOrDirectorySizeAsync(_target.DownloadFileName ??
                                                                       _target.DownloadDirectory));
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
                _downloadService?.Dispose();
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