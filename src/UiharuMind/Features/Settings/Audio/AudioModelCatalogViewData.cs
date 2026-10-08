using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Models.Downloads;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Settings.Audio;

/// <summary>
/// 获取语音模型：列在用引擎自带的模型包，下载、看进度、删除。默认只列推荐的，可展开全部
/// </summary>
public partial class AudioModelCatalogViewData : ObservableObject
{
    private readonly AudioCppEngine _engine;
    private readonly AudioCppPackageStore _store;
    private readonly DownloadQueue _queue;
    private readonly IMessageService _messageService;
    private readonly Action _onInstalledChanged;
    private List<AudioPackageRowData> _allRows = [];
    private bool _isStatusRefreshPending;

    [ObservableProperty] private bool _showAll;

    /// <param name="engine">语音引擎（提供包目录）</param>
    /// <param name="store">模型包的下载与删除</param>
    /// <param name="queue">下载队列（看进度）</param>
    /// <param name="messageService">提示与确认</param>
    /// <param name="onInstalledChanged">装上或删掉了一个模型（刷新模型选择）</param>
    public AudioModelCatalogViewData(AudioCppEngine engine, AudioCppPackageStore store, DownloadQueue queue,
        IMessageService messageService, Action onInstalledChanged)
    {
        _engine = engine;
        _store = store;
        _queue = queue;
        _messageService = messageService;
        _onInstalledChanged = onInstalledChanged;
    }

    /// <summary>
    /// 当前列出来的行
    /// </summary>
    public ObservableCollection<AudioPackageRowData> Rows { get; } = [];

    /// <summary>
    /// 有包可列（装了引擎才有 spec）
    /// </summary>
    public bool HasCatalog => _allRows.Count > 0;

    /// <summary>
    /// 「显示全部」上的数字
    /// </summary>
    public string ShowAllText => Loc.Text(LangKey.AudioCatalogShowAll, _allRows.Count);

    /// <summary>
    /// 重读在用引擎的 spec，重列
    /// </summary>
    public void Refresh()
    {
        // 推荐的在前，其次上游标了 supported 的，再按合成、识别与 id
        _allRows = _engine.GetCatalog()
            .Select(x => new AudioPackageRowData(x))
            .OrderByDescending(x => x.IsRecommended)
            .ThenByDescending(x => x.MaturityText == null)
            .ThenBy(x => x.Package.Task)
            .ThenBy(x => x.Package.Id, StringComparer.Ordinal)
            .ToList();
        ApplyFilter();
        OnPropertyChanged(nameof(HasCatalog));
        OnPropertyChanged(nameof(ShowAllText));
    }

    /// <summary>
    /// 以磁盘与下载队列为准刷新每行状态
    /// </summary>
    public void RefreshStatuses()
    {
        Dictionary<string, DownloadJob> jobs = _queue.Jobs
            .Where(x => x.State != EDownloadJobState.Completed)
            .GroupBy(x => x.Request.DestinationPath, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);
        foreach (AudioPackageRowData row in Rows)
            row.ApplyStatus(ResolveStatus(row, jobs));
    }

    [RelayCommand]
    private async Task Download(AudioPackageRowData row)
    {
        try
        {
            AudioCppPackage package = row.Package;
            IReadOnlyList<DownloadJob> jobs = await _store.EnqueueAsync(package, () =>
            {
                _engine.OnPackageInstalled(package);
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshStatuses();
                    _onInstalledChanged();
                    _messageService.ShowNotification(Loc.Text(LangKey.AudioModelInstalled, row.Title),
                        severity: MessageSeverity.Success);
                });
                return Task.CompletedTask;
            });
            foreach (DownloadJob job in jobs) job.PropertyChanged += OnJobChanged;
            RefreshStatuses();
        }
        catch (Exception e)
        {
            Log.Warning($"Download audio model failed: {row.Package.Id}, {e.Message}");
            _messageService.ShowNotification(Loc.Text(LangKey.AudioModelDownloadFailed, row.Title, e.Message),
                severity: MessageSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task Delete(AudioPackageRowData row)
    {
        if (!await _messageService.ConfirmAsync(Loc.Text(LangKey.AudioModelDeleteConfirm, row.Title))) return;
        try
        {
            _store.Delete(row.Package);
        }
        catch (IOException e)
        {
            _messageService.ShowNotification(e.Message, severity: MessageSeverity.Error);
        }

        _engine.GetModels(true);
        RefreshStatuses();
        _onInstalledChanged();
    }

    // 本机没有引擎时一键装推荐包，与模型下载自动装 llama.cpp 同一条路：排进全局队列，装好后选中
    [RelayCommand]
    private async Task InstallEngine()
    {
        try
        {
            var (state, version, job) = await Task.Run(() => _engine.Installer.EnsureQueuedAsync());
            switch (state)
            {
                case EEngineEnsureState.NoPackage:
                    _messageService.ShowNotification(Loc.Text(LangKey.AudioEngineNoPackage), severity: MessageSeverity.Warning);
                    return;
                case EEngineEnsureState.Queued when version != null && job != null:
                    _messageService.ShowNotification(Loc.Text(LangKey.AudioEngineQueued, version.Name));
                    job.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName != nameof(DownloadJob.State)) return;
                        if (job.State == EDownloadJobState.Completed)
                            Dispatcher.UIThread.Post(() => _messageService.ShowNotification(
                                Loc.Text(LangKey.ModelDownloadEngineInstalled, version.Name), severity: MessageSeverity.Success));
                        else if (job.State == EDownloadJobState.Failed)
                            Dispatcher.UIThread.Post(() => _messageService.ShowNotification(
                                Loc.Text(LangKey.ModelDownloadEngineFailed, version.Name, job.Error?.Message ?? ""),
                                severity: MessageSeverity.Error));
                    };
                    return;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Ensure audio.cpp engine failed: {e.Message}");
            _messageService.ShowNotification(Loc.Text(LangKey.AudioEngineCheckFailed, e.Message),
                severity: MessageSeverity.Warning);
        }
    }

    partial void OnShowAllChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (AudioPackageRowData row in ShowAll ? _allRows : _allRows.Where(x => x.IsRecommended))
            Rows.Add(row);
        RefreshStatuses();
        _ = LoadSizesAsync(Rows.Where(x => x.RemoteFiles == null).ToList());
    }

    // 同一仓库只列一次（PackageStore 里有缓存），拉不到的就不显示大小
    private async Task LoadSizesAsync(IReadOnlyList<AudioPackageRowData> rows)
    {
        await Task.WhenAll(rows.Select(async row =>
        {
            try
            {
                IReadOnlyList<ModelRepoFile> files = await _store.GetFilesAsync(row.Package);
                Dispatcher.UIThread.Post(() => row.ApplyRemoteFiles(files));
            }
            catch (Exception e)
            {
                Log.Debug($"List audio model files failed: {row.Package.Id}, {e.Message}");
            }
        }));
        Dispatcher.UIThread.Post(RefreshStatuses);
    }

    private QuantDownloadStatus ResolveStatus(AudioPackageRowData row, Dictionary<string, DownloadJob> jobs)
    {
        if (_store.IsInstalled(row.Package)) return new QuantDownloadStatus(EQuantDownloadState.Downloaded, 100);

        string directory = _store.PackageDirectory(row.Package);
        IEnumerable<ModelRepoFile> files = row.RemoteFiles ?? row.Package.Files.Select(x => new ModelRepoFile(x, 0, null));
        List<QuantFileSnapshot> snapshots = [];
        foreach (ModelRepoFile file in files)
        {
            if (row.Package.LocalPath(file.Path) is not { } local) continue;
            string path = Path.Combine(directory, local);
            if (File.Exists(path)) snapshots.Add(new QuantFileSnapshot(file.Size, true, null, file.Size));
            else if (jobs.TryGetValue(path, out DownloadJob? job))
                snapshots.Add(new QuantFileSnapshot(file.Size, false, job.State, job.Progress.ReceivedBytes));
            else
                snapshots.Add(new QuantFileSnapshot(file.Size, false, null,
                    FileDownloader.ReadPartialProgress(path)?.ReceivedBytes ?? 0));
        }

        // 文件都在但没有包标记（手放的、或标记还没写）：仍算没装好，避免误报可用
        QuantDownloadStatus status = QuantDownloadStatus.Resolve(snapshots, false);
        return status.State == EQuantDownloadState.Downloaded
            ? new QuantDownloadStatus(EQuantDownloadState.Partial, 99)
            : status;
    }

    // 进度回调很密（约 200ms 一次），合并成一次界面刷新
    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isStatusRefreshPending) return;
        _isStatusRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isStatusRefreshPending = false;
            RefreshStatuses();
        }, DispatcherPriority.Background);
    }
}
