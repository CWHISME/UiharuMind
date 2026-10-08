using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Generated;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Settings.Audio;

/// <summary>
/// 一个可选音色；Id 为空表示交给模型默认（服务列出的第一个）
/// </summary>
/// <param name="Id">音色 id</param>
/// <param name="DisplayName">显示名</param>
public sealed record VoiceOption(string Id, string DisplayName);

/// <summary>
/// 合成 / 听写各用哪个本地模型、合成用哪个音色
/// </summary>
public partial class AudioModelSelectionViewData : ObservableObject
{
    private readonly AudioCppEngine _engine;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _listVoices;
    private readonly IMessageService _messageService;
    private bool _isRefreshing; //回填列表时不写设置

    [ObservableProperty] private string? _selectedTtsModel;
    [ObservableProperty] private string? _selectedAsrModel;
    [ObservableProperty] private VoiceOption? _selectedVoice;
    [ObservableProperty] private bool _isLoadingVoices;

    /// <param name="engine">语音引擎</param>
    /// <param name="listVoices">列当前合成模型的音色（会按需拉起引擎）</param>
    /// <param name="messageService">提示</param>
    public AudioModelSelectionViewData(AudioCppEngine engine,
        Func<CancellationToken, Task<IReadOnlyList<string>>> listVoices, IMessageService messageService)
    {
        _engine = engine;
        _listVoices = listVoices;
        _messageService = messageService;
        Refresh();
    }

    /// <summary>
    /// 本地的语音合成模型
    /// </summary>
    public ObservableCollection<string> TtsModels { get; } = [];

    /// <summary>
    /// 本地的语音识别模型
    /// </summary>
    public ObservableCollection<string> AsrModels { get; } = [];

    /// <summary>
    /// 当前合成模型可选的音色（第一项是「默认」）
    /// </summary>
    public ObservableCollection<VoiceOption> Voices { get; } = [];

    public bool HasTtsModels => TtsModels.Count > 0;
    public bool HasAsrModels => AsrModels.Count > 0;

    /// <summary>
    /// 以模型目录为准重列，选中项取设置（没选过时引擎自己会用第一个，这里也显示第一个）
    /// </summary>
    public void Refresh()
    {
        _isRefreshing = true;
        try
        {
            IReadOnlyList<AudioCppModelEntry> models = _engine.GetModels(true);
            Fill(TtsModels, models, EAudioCppTask.Tts);
            Fill(AsrModels, models, EAudioCppTask.Asr);
            SelectedTtsModel = Pick(TtsModels, _engine.Settings.TtsModel);
            SelectedAsrModel = Pick(AsrModels, _engine.Settings.AsrModel);
            ResetVoices();
        }
        finally
        {
            _isRefreshing = false;
        }

        OnPropertyChanged(nameof(HasTtsModels));
        OnPropertyChanged(nameof(HasAsrModels));
    }

    [RelayCommand]
    private async Task LoadVoices()
    {
        if (IsLoadingVoices || SelectedTtsModel == null) return;
        IsLoadingVoices = true;
        try
        {
            IReadOnlyList<string> voices = await _listVoices(CancellationToken.None);
            _isRefreshing = true;
            ResetVoices(voices);
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
            _isRefreshing = false;
            IsLoadingVoices = false;
        }
    }

    partial void OnSelectedTtsModelChanged(string? value)
    {
        if (_isRefreshing) return;
        _engine.Settings.TtsModel = value;
        _engine.Settings.Save();
        _isRefreshing = true;
        ResetVoices();
        _isRefreshing = false;
    }

    partial void OnSelectedAsrModelChanged(string? value)
    {
        if (_isRefreshing) return;
        _engine.Settings.AsrModel = value;
        _engine.Settings.Save();
    }

    partial void OnSelectedVoiceChanged(VoiceOption? value)
    {
        if (_isRefreshing || value == null || SelectedTtsModel is not { } model) return;
        if (value.Id.Length == 0) _engine.Settings.TtsVoices.Remove(model);
        else _engine.Settings.TtsVoices[model] = value.Id;
        _engine.Settings.Save();
    }

    // 没拉过音色时只列「默认」与已选的那个，免得一打开设置就拉起引擎
    private void ResetVoices(IReadOnlyList<string>? listed = null)
    {
        string? saved = SelectedTtsModel != null && _engine.Settings.TtsVoices.TryGetValue(SelectedTtsModel, out string? voice)
            ? voice
            : null;
        Voices.Clear();
        Voices.Add(new VoiceOption("", Loc.Text(LangKey.AudioVoiceDefault)));
        foreach (string id in (listed ?? []).Append(saved ?? "").Where(x => x.Length > 0).Distinct())
            Voices.Add(new VoiceOption(id, id));
        SelectedVoice = Voices.FirstOrDefault(x => x.Id == (saved ?? "")) ?? Voices[0];
    }

    private static void Fill(ObservableCollection<string> target, IReadOnlyList<AudioCppModelEntry> models, EAudioCppTask task)
    {
        string name = task.ToConfigName();
        target.Clear();
        foreach (AudioCppModelEntry model in models.Where(x => x.Task == name).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
            target.Add(model.Id);
    }

    private static string? Pick(ObservableCollection<string> models, string? selected)
    {
        return selected != null && models.Contains(selected) ? selected : models.FirstOrDefault();
    }
}
