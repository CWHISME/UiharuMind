using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 右栏的仓库详情：量化列表、视觉投影勾选、逐行下载
/// </summary>
public partial class ModelRepoDetailData : ObservableObject, IDisposable
{
    private readonly ModelDownloadContext _context;
    private readonly IModelSource _source;
    private readonly CancellationTokenSource _cancellation = new();
    private RuntimeDeviceInfo? _device;
    private string _repoDirectory = "";

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _needsToken;
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _includeProjector = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProjectorOption))]
    private bool _isReadmeTab;
    [ObservableProperty] private bool _isReadmeLoading;
    [ObservableProperty] private string? _readme;
    [ObservableProperty] private string? _readmeStatus;
    private bool _isReadmeRequested; //说明第一次切过去才拉

    public ModelRepoDetailData(ModelDownloadContext context, IModelSource source, string repository)
    {
        _context = context;
        _source = source;
        Repository = repository;
        _context.QueueView.Changed += RefreshStatuses;
    }

    /// <summary>
    /// owner/repo
    /// </summary>
    public string Repository { get; }

    /// <summary>
    /// 仓库名（不含作者）
    /// </summary>
    public string Name => Repository[(Repository.IndexOf('/') + 1)..];

    /// <summary>
    /// 作者
    /// </summary>
    public string Owner => Repository.Contains('/') ? Repository[..Repository.IndexOf('/')] : "";

    /// <summary>
    /// 一句话概况：几个量化、有没有视觉投影
    /// </summary>
    public string SummaryText => Projector == null
        ? Loc.Text(LangKey.ModelDownloadSummary, Rows.Count)
        : Loc.Text(LangKey.ModelDownloadSummaryVision, Rows.Count);

    /// <summary>
    /// 主模型的各个量化，按大小排
    /// </summary>
    public ObservableCollection<ModelQuantRowData> Rows { get; } = [];

    /// <summary>
    /// 随主模型一起下的视觉投影；仓库没有为 null
    /// </summary>
    public ModelRepoQuant? Projector { get; private set; }

    /// <summary>
    /// 视觉投影勾选只关文件页的事，看说明时收起
    /// </summary>
    public bool ShowProjectorOption => Projector != null && !IsReadmeTab;

    /// <summary>
    /// 视觉投影勾选框的文案
    /// </summary>
    public string ProjectorText => Projector == null
        ? ""
        : Loc.Text(LangKey.ModelDownloadIncludeProjector, Projector.Quantization,
            GameUtils.FormatBytes(Projector.TotalSize));

    /// <summary>
    /// 拉仓库文件并列出量化
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            CancellationToken token = _cancellation.Token;
            string root = _context.ModelRoot();
            (IReadOnlyList<ModelRepoFile> files, IReadOnlyDictionary<string, string> localModels, RuntimeDeviceInfo device) =
                await Task.Run(async () => (
                    await _source.ListFilesAsync(Repository, token).ConfigureAwait(false),
                    _context.LocalModels(),
                    _context.DeviceInfo()), token);
            if (token.IsCancellationRequested) return;

            ModelRepoLayout layout = ModelRepoLayout.From(files);
            _device = device;
            _repoDirectory = ModelRepoDownloader.RepoDirectory(root, Repository);
            Projector = layout.PickDefaultProjector();
            OnPropertyChanged(nameof(Projector));
            OnPropertyChanged(nameof(ShowProjectorOption));
            OnPropertyChanged(nameof(ProjectorText));

            foreach (ModelRepoQuant quant in layout.Quants)
            {
                string mainPath = ModelRepoDownloader.FilePath(_repoDirectory, quant.Files[0]);
                string? conflict = localModels.TryGetValue(quant.Name, out string? path) &&
                                   !string.Equals(Path.GetFullPath(path), Path.GetFullPath(mainPath), StringComparison.Ordinal)
                    ? path
                    : null;
                Rows.Add(new ModelQuantRowData(quant, mainPath, conflict));
            }

            ModelRepoQuant? recommended = layout.PickDefaultQuant(x => EstimateRisk(x).Level);
            foreach (ModelQuantRowData row in Rows)
            {
                row.IsRecommended = row.Quant == recommended;
                row.IsSelected = row.IsRecommended;
            }

            IsEmpty = Rows.Count == 0;
            OnPropertyChanged(nameof(SummaryText));
            RefreshRisks();
            RefreshStatuses();
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            NeedsToken = true;
        }
        catch (Exception e)
        {
            Log.Warning($"List repository files failed: {Repository}, {e.Message}");
            ErrorText = Loc.Text(LangKey.ModelDownloadLoadFailed, e.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 以磁盘与队列为准刷新每行状态
    /// </summary>
    public void RefreshStatuses()
    {
        if (Rows.Count == 0) return;
        Dictionary<string, DownloadJob> jobs = _context.Queue.Jobs
            .Where(x => x.State != EDownloadJobState.Completed)
            .GroupBy(x => x.Request.DestinationPath, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);
        foreach (ModelQuantRowData row in Rows)
        {
            List<QuantFileSnapshot> files = row.Quant.Files.Select(file =>
            {
                string path = ModelRepoDownloader.FilePath(_repoDirectory, file);
                if (File.Exists(path)) return new QuantFileSnapshot(file.Size, true, null, file.Size);
                if (jobs.TryGetValue(path, out DownloadJob? job))
                    return new QuantFileSnapshot(file.Size, false, job.State, job.Progress.ReceivedBytes);
                long received = FileDownloader.ReadPartialProgress(path)?.ReceivedBytes ?? 0;
                return new QuantFileSnapshot(file.Size, false, null, received);
            }).ToList();
            row.ApplyStatus(QuantDownloadStatus.Resolve(files, row.ConflictPath != null));
        }
    }

    partial void OnIncludeProjectorChanged(bool value) => RefreshRisks();

    partial void OnIsReadmeTabChanged(bool value)
    {
        if (!value || _isReadmeRequested) return;
        _isReadmeRequested = true;
        _ = LoadReadmeAsync();
    }

    [RelayCommand]
    private void ShowFiles() => IsReadmeTab = false;

    [RelayCommand]
    private void ShowReadme() => IsReadmeTab = true;

    private async Task LoadReadmeAsync()
    {
        IsReadmeLoading = true;
        try
        {
            CancellationToken token = _cancellation.Token;
            string? readme = await Task.Run(() => _source.GetReadmeAsync(Repository, token), token);
            if (token.IsCancellationRequested) return;
            Readme = readme == null ? null : ModelReadme.Clean(readme);
            if (string.IsNullOrWhiteSpace(Readme)) ReadmeStatus = Loc.Text(LangKey.ModelDownloadNoReadme);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Warning($"Load readme failed: {Repository}, {e.Message}");
            ReadmeStatus = Loc.Text(LangKey.ModelDownloadReadmeFailed, e.Message);
        }
        finally
        {
            IsReadmeLoading = false;
        }
    }

    [RelayCommand]
    private async Task Download(ModelQuantRowData row)
    {
        if (!row.Status.CanDownload) return;
        // 引擎包要排在模型前面：队列一次只跑一项，先排先下
        await EnsureEngineAsync();
        ModelRepoQuant? projector = IncludeProjector ? Projector : null;
        string name = row.Quant.Name;
        _context.Downloader.Enqueue(_source, Repository, _context.ModelRoot(), row.Quant, projector,
            () => UiDispatcher.InvokeAsyncTask(async () =>
            {
                await _context.RefreshLocalModels();
                _context.Messages.ShowNotification(Loc.Text(LangKey.ModelDownloadCompleted, name));
            }));
        RefreshStatuses();
    }

    // 本机没有引擎时不问，直接把推荐包一起排进去；拉不到包也不拦着下模型
    private async Task EnsureEngineAsync()
    {
        try
        {
            var (state, version, job) = await Task.Run(_context.EnsureEngine);
            if (state == EEngineEnsureState.NoPackage)
            {
                _context.Messages.ShowNotification(Loc.Text(LangKey.ModelDownloadEngineNoPackage), severity: MessageSeverity.Warning);
                return;
            }

            if (state != EEngineEnsureState.Queued || version == null || job == null) return;
            _context.Messages.ShowNotification(Loc.Text(LangKey.ModelDownloadEngineQueued, version.Name));
            job.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(DownloadJob.State)) return;
                if (job.State == EDownloadJobState.Completed)
                    UiDispatcher.FireAndForget(() => _context.Messages.ShowNotification(
                        Loc.Text(LangKey.ModelDownloadEngineInstalled, version.Name), severity: MessageSeverity.Success));
                else if (job.State == EDownloadJobState.Failed)
                    UiDispatcher.FireAndForget(() => _context.Messages.ShowNotification(
                        Loc.Text(LangKey.ModelDownloadEngineFailed, version.Name, job.Error?.Message ?? ""),
                        severity: MessageSeverity.Error));
            };
        }
        catch (Exception e)
        {
            Log.Warning($"Ensure runtime engine failed: {e.Message}");
            _context.Messages.ShowNotification(Loc.Text(LangKey.ModelDownloadEngineCheckFailed, e.Message),
                severity: MessageSeverity.Warning);
        }
    }

    [RelayCommand]
    private void OpenSourceSettings() => _context.OpenSourceSettings();

    private void RefreshRisks()
    {
        if (_device == null) return;
        foreach (ModelQuantRowData row in Rows)
        {
            (RuntimeLoadRiskLevel level, long bytes) = EstimateRisk(row.Quant);
            row.ApplyRisk(level, bytes, _device);
        }
    }

    private (RuntimeLoadRiskLevel Level, long Bytes) EstimateRisk(ModelRepoQuant quant)
    {
        long projectorBytes = IncludeProjector ? Projector?.TotalSize ?? 0 : 0;
        return (RuntimeLoadRiskEvaluator.EstimateBeforeDownload(quant.TotalSize, projectorBytes, _device!),
            RuntimeLoadRiskEvaluator.EstimateBytesBeforeDownload(quant.TotalSize, projectorBytes));
    }

    public void Dispose()
    {
        _context.QueueView.Changed -= RefreshStatuses;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
