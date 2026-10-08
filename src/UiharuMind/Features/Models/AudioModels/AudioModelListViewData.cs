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
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Models.AudioModels.Online;
using UiharuMind.Features.Models.AudioModels.Voices;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.AudioModels;

/// <summary>
/// 「语音模型」页签：本机装了的语音合成 / 识别模型，设朗读与听写默认用哪个、选音色、删除。
/// 下载在「获取模型」的语音分类里
/// </summary>
public partial class AudioModelListViewData : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly AudioCppEngine _engine;
    private readonly AudioCppPackageStore _store;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _listVoices;
    private readonly IReadAloudService _readAloud;
    private readonly Action<string> _openFolder;
    private readonly Action _goToMarket;
    private readonly Action _openEngineSettings;
    private readonly Dictionary<EAudioCapability, string?> _defaultModels = new(); //引擎会用的本地模型（选了的，没选就第一个）

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasEngine = true;
    [ObservableProperty] private bool _hasModels;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private string _engineStatusText = "";
    [ObservableProperty] private bool _isEngineRunning;

    /// <param name="messageService">提示与确认</param>
    /// <param name="engine">语音引擎</param>
    /// <param name="store">模型包的删除</param>
    /// <param name="listVoices">列某个合成模型的音色（会按需拉起引擎）</param>
    /// <param name="readAloud">试听（与消息朗读同一路：点试听会立刻停掉正在念的消息）</param>
    /// <param name="openFolder">打开目录</param>
    /// <param name="goToMarket">去「获取模型」的语音分类</param>
    /// <param name="openEngineSettings">去设置里的「本地模型（语音）」</param>
    /// <param name="voices">音色库；null 不显示</param>
    /// <param name="online">在线服务；null 不显示</param>
    public AudioModelListViewData(IMessageService messageService, AudioCppEngine engine, AudioCppPackageStore store,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> listVoices, IReadAloudService readAloud,
        Action<string> openFolder, Action goToMarket, Action openEngineSettings, VoiceLibraryViewData? voices = null,
        AudioServiceListViewData? online = null)
    {
        Voices = voices;
        if (voices != null) voices.Refreshed += ApplyLibraryVoices;
        Online = online;
        if (online != null) online.Changed += OnOnlineChanged;
        _readAloud = readAloud;
        readAloud.StateChanged += OnReadAloudChanged;
        _messageService = messageService;
        _engine = engine;
        _store = store;
        _listVoices = listVoices;
        _openFolder = openFolder;
        _goToMarket = goToMarket;
        _openEngineSettings = openEngineSettings;
    }

    /// <summary>
    /// 语音合成（朗读）模型
    /// </summary>
    public ObservableCollection<AudioModelItemData> TtsModels { get; } = [];

    /// <summary>
    /// 语音识别（听写）模型
    /// </summary>
    public ObservableCollection<AudioModelItemData> AsrModels { get; } = [];

    public bool HasTtsModels => TtsModels.Count > 0;

    /// <summary>
    /// 在线服务；没配为 null
    /// </summary>
    public AudioServiceListViewData? Online { get; }

    /// <summary>
    /// 朗读分组里有东西（本地模型或在线服务）
    /// </summary>
    public bool HasTts => HasTtsModels || Online?.TtsServices.Count > 0;

    /// <summary>
    /// 听写分组里有东西
    /// </summary>
    public bool HasAsr => HasAsrModels || Online?.AsrServices.Count > 0;

    /// <summary>
    /// 本地模型、在线服务至少有一个
    /// </summary>
    public bool HasAnything => HasTts || HasAsr;

    /// <summary>
    /// 音色库；没配为 null
    /// </summary>
    public VoiceLibraryViewData? Voices { get; }

    /// <summary>
    /// 有会克隆的合成模型时才显示音色库
    /// </summary>
    public bool ShowVoices => Voices != null && TtsModels.Any(x => x.CanClone);

    /// <summary>
    /// 能抽卡的模型，默认合成模型排最前
    /// </summary>
    public IReadOnlyList<VoiceDrawModel> DrawModels => TtsModels.Where(x => x.CanDraw)
        .OrderByDescending(x => x.IsDefault).Select(x => new VoiceDrawModel(x.Name, x.FamilyName)).ToList();

    public bool HasAsrModels => AsrModels.Count > 0;

    /// <summary>
    /// 一个模型都没有：显示空状态
    /// </summary>
    public bool ShowEmpty => IsLoaded && !HasAnything;

    /// <summary>
    /// 语音模型目录
    /// </summary>
    public string ModelsDirectory => _store.RootDirectory;

    /// <summary>
    /// 重扫模型目录（会读文件头，放在后台）
    /// </summary>
    public async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            var (models, families, sizes, defaults) = await Task.Run(async () =>
            {
                if (_engine.CurrentVersion == null) await _engine.GetLocalVersionsAsync().ConfigureAwait(false);
                IReadOnlyList<AudioCppModelEntry> scanned = _engine.GetModels(true);
                Dictionary<string, AudioCppFamily> byFamily = _engine.GetFamilies()
                    .GroupBy(x => x.Family, StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
                Dictionary<string, long> fileSizes = scanned.ToDictionary(x => x.Id, x => FileSize(x.ModelFile));
                return (scanned, byFamily, fileSizes,
                    (Tts: DefaultModel(EAudioCppTask.Tts), Asr: DefaultModel(EAudioCppTask.Asr)));
            });

            HasEngine = _engine.CurrentVersion != null;
            _defaultModels[EAudioCapability.Tts] = defaults.Tts;
            _defaultModels[EAudioCapability.Asr] = defaults.Asr;
            Fill(TtsModels, models, families, sizes, EAudioCppTask.Tts);
            Fill(AsrModels, models, families, sizes, EAudioCppTask.Asr);
            HasModels = TtsModels.Count + AsrModels.Count > 0;
            OnPropertyChanged(nameof(HasTtsModels));
            OnPropertyChanged(nameof(HasAsrModels));
            OnPropertyChanged(nameof(ShowVoices));
            OnOnlineChanged();
            if (Voices != null) Voices.Refresh();
            await RefreshStatusAsync();
        }
        catch (Exception e)
        {
            Log.Warning($"Scan audio models failed: {e.Message}");
        }
        finally
        {
            IsLoading = false;
            IsLoaded = true;
        }
    }

    /// <summary>
    /// 刷新引擎状态与各模型「已加载」标记（只问已经跑着的引擎，不会把它拉起来）
    /// </summary>
    public async Task RefreshStatusAsync()
    {
        AudioCppEngineStatus status = await _engine.GetStatusAsync(CancellationToken.None);
        IsEngineRunning = status.State == EAudioEngineRunState.Running;
        EngineStatusText = status.State switch
        {
            EAudioEngineRunState.Starting => Loc.Text(LangKey.AudioEngineStatusStarting),
            EAudioEngineRunState.Running => Loc.Text(LangKey.AudioEngineStatusRunning,
                (status.Backend ?? "").ToUpperInvariant(), status.LoadedModels.Count, _engine.Settings.IdleUnloadMinutes),
            _ => Loc.Text(LangKey.AudioEngineStatusStopped)
        };
        foreach (AudioModelItemData item in TtsModels.Concat(AsrModels))
            item.IsLoaded = status.LoadedModels.Contains(item.Name);
    }

    partial void OnHasModelsChanged(bool value) => OnPropertyChanged(nameof(ShowEmpty));

    partial void OnIsLoadedChanged(bool value) => OnPropertyChanged(nameof(ShowEmpty));

    // 本地模型与在线服务二选一：设本地的就把默认改回本地引擎
    [RelayCommand]
    private void SetDefault(AudioModelItemData item)
    {
        bool isTts = item.IsTts;
        if (isTts) _engine.Settings.TtsModel = item.Name;
        else _engine.Settings.AsrModel = item.Name;
        _engine.Settings.Save();
        _defaultModels[isTts ? EAudioCapability.Tts : EAudioCapability.Asr] = item.Name;
        Online?.Router.UseLocal(isTts ? EAudioCapability.Tts : EAudioCapability.Asr);
        RefreshLocalDefaults();
        _messageService.ShowNotification(Loc.Text(isTts ? LangKey.AudioDefaultSetTts : LangKey.AudioDefaultSetAsr,
            item.Name), severity: MessageSeverity.Success);
    }

    [RelayCommand]
    private async Task LoadVoices(AudioModelItemData item)
    {
        if (item.IsLoadingVoices) return;
        item.IsLoadingVoices = true;
        try
        {
            item.FillVoices(await _listVoices(item.Name, CancellationToken.None));
        }
        catch (Exception e) when (AudioErrorText.IsExpected(e))
        {
            _messageService.ShowNotification(Loc.Text(LangKey.AudioVoicesLoadFailed, AudioErrorText.Describe(e)),
                severity: MessageSeverity.Warning);
        }
        finally
        {
            item.IsLoadingVoices = false;
        }
    }

    // 再点一下停；换一行试听、或去念消息都会打断它。失败的提示由朗读服务出
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Preview(AudioModelItemData item)
    {
        string? voice = item.SelectedVoice is { Id.Length: > 0 } selected ? selected.Id : null;
        await _readAloud.ToggleAsync(item, item.PreviewText, new ReadAloudOptions(item.Name, voice));
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private Task AddService() => Online?.AddCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    private void OnOnlineChanged()
    {
        RefreshLocalDefaults();
        OnPropertyChanged(nameof(HasTts));
        OnPropertyChanged(nameof(HasAsr));
        OnPropertyChanged(nameof(HasAnything));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    // 默认用在线服务时，本地模型都不标「默认」
    private void RefreshLocalDefaults()
    {
        foreach ((EAudioCapability task, ObservableCollection<AudioModelItemData> items) in
                 new[] { (EAudioCapability.Tts, TtsModels), (EAudioCapability.Asr, AsrModels) })
        {
            bool online = Online?.Router.DefaultService(task) != null;
            string? model = _defaultModels.GetValueOrDefault(task);
            foreach (AudioModelItemData item in items) item.IsDefault = !online && item.Name == model;
        }
    }

    private void ApplyLibraryVoices()
    {
        IReadOnlyList<string> names = Voices?.Names ?? [];
        foreach (AudioModelItemData item in TtsModels) item.SetLibraryVoices(names);
    }

    private void OnReadAloudChanged()
    {
        foreach (AudioModelItemData item in TtsModels)
            item.IsPreviewing = ReferenceEquals(_readAloud.Owner, item) && _readAloud.State != EReadAloudState.Idle;
    }

    [RelayCommand]
    private async Task Delete(AudioModelItemData item)
    {
        if (!await _messageService.ConfirmAsync(Loc.Text(LangKey.AudioModelDeleteConfirm, item.Name))) return;
        try
        {
            string file = item.Entry.ModelFile ?? item.Entry.Path;
            await Task.Run(() => _store.DeleteContaining(file));
        }
        catch (IOException e)
        {
            _messageService.ShowNotification(e.Message, severity: MessageSeverity.Error);
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenFolder(AudioModelItemData? item)
    {
        string? file = item?.Entry.ModelFile;
        _openFolder(file != null ? Path.GetDirectoryName(file) ?? file : ModelsDirectory);
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    // 停掉引擎：模型占的内存全部还回去，下次朗读、听写或试听时自动再起
    [RelayCommand]
    private async Task StopEngine()
    {
        _readAloud.Stop();
        _engine.Stop();
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private void GoToMarket() => _goToMarket();

    [RelayCommand]
    private void OpenEngineSettings() => _openEngineSettings();

    // 引擎实际会用的那个：设置里选的，没选或不在了就第一个
    private string? DefaultModel(EAudioCppTask task)
    {
        try
        {
            return _engine.ResolveModel(task).Id;
        }
        catch (AudioModelMissingException)
        {
            return null;
        }
    }

    private void Fill(ObservableCollection<AudioModelItemData> target, IReadOnlyList<AudioCppModelEntry> models,
        Dictionary<string, AudioCppFamily> families, Dictionary<string, long> sizes, EAudioCppTask task)
    {
        string name = task.ToConfigName();
        target.Clear();
        foreach (AudioCppModelEntry model in models.Where(x => x.Task == name)
                     .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            AudioCppFamily? family = families.GetValueOrDefault(model.Family);
            target.Add(new AudioModelItemData(model, family, sizes.GetValueOrDefault(model.Id), _engine.Settings));
        }
    }

    private static long FileSize(string? path)
    {
        try
        {
            return path != null && File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
