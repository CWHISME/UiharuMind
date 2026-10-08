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
    private readonly Action<string> _openFolder;
    private readonly Action _goToMarket;
    private readonly Action _openEngineSettings;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasEngine = true;
    [ObservableProperty] private bool _hasModels;
    [ObservableProperty] private bool _isLoaded;

    /// <param name="messageService">提示与确认</param>
    /// <param name="engine">语音引擎</param>
    /// <param name="store">模型包的删除</param>
    /// <param name="listVoices">列某个合成模型的音色（会按需拉起引擎）</param>
    /// <param name="openFolder">打开目录</param>
    /// <param name="goToMarket">去「获取模型」的语音分类</param>
    /// <param name="openEngineSettings">去设置里的「本地模型（语音）」</param>
    public AudioModelListViewData(IMessageService messageService, AudioCppEngine engine, AudioCppPackageStore store,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> listVoices, Action<string> openFolder,
        Action goToMarket, Action openEngineSettings)
    {
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

    public bool HasAsrModels => AsrModels.Count > 0;

    /// <summary>
    /// 一个模型都没有：显示空状态
    /// </summary>
    public bool ShowEmpty => IsLoaded && !HasModels;

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
            Fill(TtsModels, models, families, sizes, EAudioCppTask.Tts, defaults.Tts);
            Fill(AsrModels, models, families, sizes, EAudioCppTask.Asr, defaults.Asr);
            HasModels = TtsModels.Count + AsrModels.Count > 0;
            OnPropertyChanged(nameof(HasTtsModels));
            OnPropertyChanged(nameof(HasAsrModels));
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

    partial void OnHasModelsChanged(bool value) => OnPropertyChanged(nameof(ShowEmpty));

    partial void OnIsLoadedChanged(bool value) => OnPropertyChanged(nameof(ShowEmpty));

    [RelayCommand]
    private void SetDefault(AudioModelItemData item)
    {
        bool isTts = item.IsTts;
        if (isTts) _engine.Settings.TtsModel = item.Name;
        else _engine.Settings.AsrModel = item.Name;
        _engine.Settings.Save();
        foreach (AudioModelItemData model in isTts ? TtsModels : AsrModels) model.IsDefault = model == item;
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
        catch (Exception e) when (e is LocalEngineNotReadyException or LocalServerException or AudioModelMissingException
                                      or System.Net.Http.HttpRequestException)
        {
            string detail = e is LocalServerException server ? server.Detail : e.Message;
            _messageService.ShowNotification(Loc.Text(LangKey.AudioVoicesLoadFailed, detail),
                severity: MessageSeverity.Warning);
        }
        finally
        {
            item.IsLoadingVoices = false;
        }
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
        Dictionary<string, AudioCppFamily> families, Dictionary<string, long> sizes, EAudioCppTask task,
        string? defaultModel)
    {
        string name = task.ToConfigName();
        target.Clear();
        foreach (AudioCppModelEntry model in models.Where(x => x.Task == name)
                     .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            AudioCppFamily? family = families.GetValueOrDefault(model.Family);
            target.Add(new AudioModelItemData(model, family?.DisplayName ?? model.Family, sizes.GetValueOrDefault(model.Id),
                family?.BuiltinVoices ?? [], _engine.Settings) { IsDefault = model.Id == defaultModel });
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
