using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Models.Downloads.Audio;

/// <summary>
/// 按任务筛语音模型的一个选项
/// </summary>
/// <param name="Task">任务；null 为全部</param>
/// <param name="Label">显示文字</param>
public sealed record AudioTaskFilterOption(EAudioCppTask? Task, string Label);

/// <summary>
/// 「获取模型」的语音模型分类：左边是在用 audio.cpp 引擎自带 spec 里的模型家族，右边看家族详情与各个包。
/// 模型清单随引擎走，没装引擎时引导先装
/// </summary>
public partial class AudioMarketData : ObservableObject, IModelMarket
{
    private readonly ModelDownloadContext _context;
    private List<AudioFamilyListItem> _all = [];
    private bool _isLoaded;
    private string _sourceKey = "";

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private AudioTaskFilterOption _selectedFilter;
    [ObservableProperty] private AudioFamilyListItem? _selectedFamily;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private AudioFamilyDetailData? _detail;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnginePrompt))]
    private bool _isLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnginePrompt))]
    private bool _hasEngine = true;
    [ObservableProperty] private bool _isEngineQueued;
    [ObservableProperty] private bool _isResultEmpty;
    [ObservableProperty] private string _sourceLabel;

    public AudioMarketData(ModelDownloadContext context)
    {
        _context = context;
        FilterOptions =
        [
            new AudioTaskFilterOption(null, Loc.Text(LangKey.AudioMarketFilterAll)),
            new AudioTaskFilterOption(EAudioCppTask.Tts, Loc.Text(LangKey.AudioMarketFilterTts)),
            new AudioTaskFilterOption(EAudioCppTask.Asr, Loc.Text(LangKey.AudioMarketFilterAsr))
        ];
        _selectedFilter = FilterOptions[0];
        _sourceLabel = ModelDownloadSourceInfo.Label();
    }

    public string Title => Loc.Text(LangKey.ModelMarketAudio);

    public bool HasDetail => Detail != null;

    /// <summary>
    /// 筛选项
    /// </summary>
    public AudioTaskFilterOption[] FilterOptions { get; }

    /// <summary>
    /// 筛过的家族
    /// </summary>
    public ObservableCollection<AudioFamilyListItem> Families { get; } = [];

    /// <summary>
    /// 没装引擎：列表位置换成安装引导
    /// </summary>
    public bool ShowEnginePrompt => !HasEngine && !IsLoading;

    public void Activate()
    {
        string key = ModelDownloadSourceInfo.Key();
        if (_isLoaded && key == _sourceKey)
        {
            _ = RefreshInstalledAsync();
            return;
        }

        // 引擎装好或换了版本，模型清单跟着变
        if (!_isLoaded)
            _context.AudioEngine.Distribution.VersionChanged += () => UiDispatcher.FireAndForget(() => _ = ReloadAsync());
        _sourceKey = key;
        SourceLabel = ModelDownloadSourceInfo.Label();
        _isLoaded = true;
        _ = ReloadAsync();
    }

    public void CloseDetail()
    {
        Detail?.Dispose();
        Detail = null;
        SelectedFamily = null;
    }

    /// <summary>
    /// 重读在用引擎的 spec 与本机已装的包
    /// </summary>
    public async Task ReloadAsync()
    {
        IsLoading = true;
        try
        {
            AudioCppEngine engine = _context.AudioEngine;
            (IReadOnlyList<AudioCppFamily> families, HashSet<string> installed) = await Task.Run(async () =>
            {
                if (engine.Distribution.CurrentVersion == null) await engine.Distribution.ScanAsync().ConfigureAwait(false);
                IReadOnlyList<AudioCppFamily> result = engine.Models.GetFamilies();
                return (result, InstalledFamilies(result));
            });

            HasEngine = engine.Distribution.CurrentVersion != null;
            // 推荐的在前，其次上游标了 supported 的，再按名称
            _all = families
                .Select(x => new AudioFamilyListItem(x) { IsInstalled = installed.Contains(x.Family) })
                .OrderByDescending(x => x.IsRecommended)
                .ThenByDescending(x => x.MaturityText == null)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            // 换了引擎版本，详情里的包也可能变了：关掉，免得对着旧 spec 下载
            CloseDetail();
            ApplyFilter();
        }
        catch (Exception e)
        {
            Log.Warning($"Load audio model catalog failed: {e.Message}");
        }
        finally
        {
            IsLoading = false;
            IsResultEmpty = HasEngine && Families.Count == 0;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFilterChanged(AudioTaskFilterOption value) => ApplyFilter();

    partial void OnSelectedFamilyChanged(AudioFamilyListItem? value)
    {
        if (value == null || value.Family == Detail?.Family) return;
        Detail?.Dispose();
        AudioFamilyDetailData detail = new(_context, value.Family, () => _ = RefreshInstalledAsync());
        Detail = detail;
        _ = detail.LoadAsync();
    }

    // 本机没有引擎时一键装推荐包，与下载对话模型时自动装 llama.cpp 同一条路：排进全局队列，装好后选中
    [RelayCommand]
    private async Task InstallEngine()
    {
        IsEngineQueued = true;
        try
        {
            var (state, version, job) = await Task.Run(() => _context.AudioEngine.Distribution.Installer.EnsureQueuedAsync());
            switch (state)
            {
                case EEngineEnsureState.NoPackage:
                    IsEngineQueued = false;
                    _context.Messages.ShowNotification(Loc.Text(LangKey.AudioEngineNoPackage), severity: MessageSeverity.Warning);
                    return;
                case EEngineEnsureState.AlreadyInstalled:
                    await ReloadAsync();
                    return;
                case EEngineEnsureState.Queued when version != null && job != null:
                    _context.Messages.ShowNotification(Loc.Text(LangKey.AudioEngineQueued, version.Name));
                    job.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName != nameof(DownloadJob.State) || job.State != EDownloadJobState.Failed) return;
                        UiDispatcher.FireAndForget(() =>
                        {
                            IsEngineQueued = false;
                            _context.Messages.ShowNotification(
                                Loc.Text(LangKey.ModelDownloadEngineFailed, version.Name, job.Error?.Message ?? ""),
                                severity: MessageSeverity.Error);
                        });
                    };
                    return;
            }
        }
        catch (Exception e)
        {
            IsEngineQueued = false;
            Log.Warning($"Ensure audio.cpp engine failed: {e.Message}");
            _context.Messages.ShowNotification(Loc.Text(LangKey.AudioEngineCheckFailed, e.Message),
                severity: MessageSeverity.Warning);
        }
    }

    [RelayCommand]
    private void OpenSourceSettings() => _context.OpenSourceSettings();

    private void ApplyFilter()
    {
        string query = SearchText.Trim();
        AudioFamilyListItem? selected = SelectedFamily;
        Families.Clear();
        foreach (AudioFamilyListItem item in _all.Where(x =>
                     (SelectedFilter.Task == null || x.Family.Task == SelectedFilter.Task) && x.Matches(query)))
            Families.Add(item);
        if (selected != null && Families.Contains(selected)) SelectedFamily = selected;
        IsResultEmpty = HasEngine && !IsLoading && Families.Count == 0;
    }

    private async Task RefreshInstalledAsync()
    {
        List<AudioFamilyListItem> items = _all;
        HashSet<string> installed = await Task.Run(() => InstalledFamilies(items.Select(x => x.Family).ToList()));
        foreach (AudioFamilyListItem item in items) item.IsInstalled = installed.Contains(item.Family.Family);
        Detail?.RefreshStatuses();
    }

    private HashSet<string> InstalledFamilies(IReadOnlyList<AudioCppFamily> families) =>
        families.Where(x => x.Packages.Any(_context.AudioStore.IsInstalled)).Select(x => x.Family).ToHashSet();
}
