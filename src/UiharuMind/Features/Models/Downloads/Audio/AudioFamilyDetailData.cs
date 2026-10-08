using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Models.Downloads.Audio;

/// <summary>
/// 右栏的语音模型家族详情：说明、能力、语种与各个包（逐行下载、设为默认）
/// </summary>
public partial class AudioFamilyDetailData : ObservableObject, IDisposable
{
    private readonly ModelDownloadContext _context;
    private readonly Action _onInstalledChanged;
    private readonly CancellationTokenSource _cancellation = new();

    [ObservableProperty] private bool _isLoading = true;

    /// <param name="context">共用依赖</param>
    /// <param name="family">家族</param>
    /// <param name="onInstalledChanged">装上了一个包（刷新列表上的已安装标记）</param>
    public AudioFamilyDetailData(ModelDownloadContext context, AudioCppFamily family, Action onInstalledChanged)
    {
        _context = context;
        _onInstalledChanged = onInstalledChanged;
        Family = family;
        AudioCppPackage? recommended = family.DefaultPackage;
        foreach (AudioCppPackage package in family.Packages)
            Rows.Add(new AudioPackageRowData(package, family.DisplayName) { IsRecommended = package == recommended });
        Tags = AudioModelText.FeatureTags(family);
        _context.QueueView.Changed += RefreshStatuses;
        RefreshStatuses();
    }

    /// <summary>
    /// 家族
    /// </summary>
    public AudioCppFamily Family { get; }

    public string Title => Family.DisplayName;

    /// <summary>
    /// 概况：合成 · 5 个版本，非 supported 的再带上成熟度
    /// </summary>
    public string SummaryText
    {
        get
        {
            string summary = Loc.Text(LangKey.AudioFamilySummary, AudioModelText.TaskText(Family.Task), Rows.Count);
            return AudioModelText.MaturityText(Family.Status) is { } maturity ? $"{summary}  ·  {maturity}" : summary;
        }
    }

    /// <summary>
    /// 能力标签
    /// </summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// 上游说明（英文原文）
    /// </summary>
    public string Description => Family.Description;

    public string LanguagesText => AudioModelText.LanguagesText(Family.Languages);

    /// <summary>
    /// 预置音色；没有为 null
    /// </summary>
    public string? VoicesText => AudioModelText.VoicesText(Family.BuiltinVoices);

    /// <summary>
    /// 全部预置音色（悬停提示）；没有为 null
    /// </summary>
    public string? AllVoicesText => Family.BuiltinVoices.Count == 0 ? null : string.Join(", ", Family.BuiltinVoices);

    public bool NeedsEspeak => Family.NeedsEspeak;

    /// <summary>
    /// 各个包
    /// </summary>
    public ObservableCollection<AudioPackageRowData> Rows { get; } = [];

    /// <summary>
    /// 拉各包的文件大小，估这台机器跑不跑得动
    /// </summary>
    public async Task LoadAsync()
    {
        CancellationToken token = _cancellation.Token;
        try
        {
            RuntimeDeviceInfo device = await Task.Run(_context.DeviceInfo, token);
            // 同一仓库只列一次（PackageStore 里有缓存），拉不到的那行就不显示大小
            await Task.WhenAll(Rows.Select(async row =>
            {
                try
                {
                    IReadOnlyList<ModelRepoFile> files = await _context.AudioStore.GetFilesAsync(row.Package, token);
                    await UiDispatcher.InvokeAsync(() =>
                    {
                        row.ApplyRemoteFiles(files);
                        long size = files.Sum(x => x.Size);
                        row.ApplyRisk(RuntimeLoadRiskEvaluator.EstimateBeforeDownload(size, 0, device),
                            RuntimeLoadRiskEvaluator.EstimateBytesBeforeDownload(size, 0), device);
                    });
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Log.Debug($"List audio model files failed: {row.Package.Id}, {e.Message}");
                }
            }));
            if (!token.IsCancellationRequested) RefreshStatuses();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 以磁盘与下载队列为准刷新每行状态，顺带标出朗读 / 听写默认用的那个
    /// </summary>
    public void RefreshStatuses()
    {
        Dictionary<string, DownloadJob> jobs = _context.Queue.Jobs
            .Where(x => x.State != EDownloadJobState.Completed)
            .GroupBy(x => x.Request.DestinationPath, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);
        string? defaultModel = _context.AudioDefaults.Resolve(Family.Task);
        foreach (AudioPackageRowData row in Rows)
        {
            row.ApplyStatus(ResolveStatus(row, jobs));
            row.IsDefault = row.IsDownloaded && row.Package.ModelName == defaultModel;
        }
    }

    [RelayCommand]
    private async Task Download(AudioPackageRowData row)
    {
        if (!row.CanDownload) return;
        try
        {
            AudioCppPackage package = row.Package;
            await _context.AudioStore.EnqueueAsync(package, () =>
            {
                _context.AudioEngine.OnPackageInstalled(package);
                UiDispatcher.FireAndForget(() =>
                {
                    RefreshStatuses();
                    _onInstalledChanged();
                    _context.Messages.ShowNotification(Loc.Text(LangKey.AudioModelInstalled, row.Label),
                        severity: MessageSeverity.Success);
                });
                return Task.CompletedTask;
            });
            RefreshStatuses();
        }
        catch (Exception e)
        {
            Log.Warning($"Download audio model failed: {row.Package.Id}, {e.Message}");
            _context.Messages.ShowNotification(Loc.Text(LangKey.AudioModelDownloadFailed, row.Label, e.Message),
                severity: MessageSeverity.Error);
        }
    }

    [RelayCommand]
    private void SetDefault(AudioPackageRowData row)
    {
        if (!row.CanSetDefault) return;
        bool isTts = row.Package.Task == EAudioCppTask.Tts;
        _context.AudioDefaults.Use(row.Package.Task, row.Package.ModelName);
        RefreshStatuses();
        _context.Messages.ShowNotification(Loc.Text(isTts ? LangKey.AudioDefaultSetTts : LangKey.AudioDefaultSetAsr,
            row.Package.ModelName), severity: MessageSeverity.Success);
    }

    [RelayCommand]
    private void OpenSourceSettings() => _context.OpenSourceSettings();


    private QuantDownloadStatus ResolveStatus(AudioPackageRowData row, Dictionary<string, DownloadJob> jobs)
    {
        AudioCppPackageStore store = _context.AudioStore;
        if (store.IsInstalled(row.Package)) return new QuantDownloadStatus(EQuantDownloadState.Downloaded, 100);

        string directory = store.PackageDirectory(row.Package);
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

    public void Dispose()
    {
        _context.QueueView.Changed -= RefreshStatuses;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
