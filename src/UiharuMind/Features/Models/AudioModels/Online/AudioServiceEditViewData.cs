using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Models;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Models.Shared;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.AudioModels.Online;

/// <summary>
/// 预设列表里的一项
/// </summary>
/// <param name="Preset">预设</param>
/// <param name="DisplayName">显示名：服务商 · 朗读 / 听写</param>
public sealed record AudioServicePresetItem(AudioServicePreset Preset, string DisplayName);

/// <summary>
/// 任务下拉的一项
/// </summary>
/// <param name="Task">合成 / 识别</param>
/// <param name="Label">显示名</param>
public sealed record AudioTaskOption(EAudioCapability Task, string Label);

/// <summary>
/// 在线语音服务的新建、编辑表单。纯逻辑：开窗口、跳官网归窗口后台代码。
/// 确认时产出一个新实例，由列表整项替换——不就地改活实例
/// </summary>
public partial class AudioServiceEditViewData : ObservableObject
{
    private readonly AudioServiceInfo? _source;
    private readonly IReadOnlyCollection<string> _takenNames;
    private readonly IReadOnlyList<RemoteModelInfo> _chatModels;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _edgeVoices;
    private readonly Func<AudioServiceInfo, CancellationToken, Task> _preview;
    private readonly IMessageService _messages;
    private readonly CancellationTokenSource _lifetime = new(); //关窗时取消在途的试听、拉音色
    private CancellationTokenSource? _previewing; //在途的试听，再点一下取消它
    private string _autoName = string.Empty; //随预设自动填的名字；用户改过就不再跟

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WebsiteUrl), nameof(HasWebsite), nameof(IsEdge), nameof(IsCompatible),
        nameof(CanChooseTask), nameof(HasEndpointError), nameof(HasModelIdError), nameof(CanConfirm))]
    private AudioServicePresetItem? _selectedPreset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTts))]
    private AudioTaskOption _selectedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNameError), nameof(CanConfirm))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEndpointError), nameof(CanConfirm), nameof(CopySources), nameof(HasCopySources))]
    private string _endpoint = string.Empty;

    [ObservableProperty] private string _apiKey = string.Empty;

    [ObservableProperty] private bool _isPreviewing;

    [ObservableProperty] private bool _isLoadingVoices;

    /// <param name="source">要编辑的服务；null 为新建</param>
    /// <param name="takenNames">其他服务已用的名字</param>
    /// <param name="chatModels">已有的远程对话模型，供沿用密钥</param>
    /// <param name="edgeVoices">列 Edge TTS 的全部音色</param>
    /// <param name="preview">用表单当前的样子念一句试听（合成服务才有）</param>
    /// <param name="messages">失败提示</param>
    public AudioServiceEditViewData(AudioServiceInfo? source, IReadOnlyCollection<string> takenNames,
        IReadOnlyList<RemoteModelInfo> chatModels, Func<CancellationToken, Task<IReadOnlyList<string>>> edgeVoices,
        Func<AudioServiceInfo, CancellationToken, Task> preview, IMessageService messages)
    {
        _source = source;
        _takenNames = takenNames;
        _chatModels = chatModels;
        _edgeVoices = edgeVoices;
        _preview = preview;
        _messages = messages;
        TaskOptions =
        [
            new AudioTaskOption(EAudioCapability.Tts, Loc.Text(LangKey.AudioServiceTaskTts)),
            new AudioTaskOption(EAudioCapability.Asr, Loc.Text(LangKey.AudioServiceTaskAsr))
        ];
        _selectedTask = TaskOptions[0];
        Presets = AudioServicePreset.All.Select(p => new AudioServicePresetItem(p, PresetName(p))).ToList();
        IsEditMode = source != null;
        ModelId.Changed += OnModelIdChanged;
        Voice.Changed += _ => StopPreview();

        if (source == null)
        {
            SelectedPreset = Presets[0];
            return;
        }

        // 编辑时只借预设的官网与候选，不回填任何字段
        _selectedPreset = Presets.First(x => x.Preset == AudioServicePreset.Match(source));
        _selectedTask = TaskOptions.First(x => x.Task == source.Task);
        Name = source.Name;
        Endpoint = source.Endpoint;
        ApiKey = source.ApiKey;
        ModelId.Text = source.ModelId;
        Voice.Text = source.Voice;
        LoadOptions(_selectedPreset.Preset);
    }

    /// <summary>编辑已有服务（服务商不可换）</summary>
    public bool IsEditMode { get; }

    /// <summary>服务商预设</summary>
    public IReadOnlyList<AudioServicePresetItem> Presets { get; }

    /// <summary>任务候选（只有自定义能选）</summary>
    public IReadOnlyList<AudioTaskOption> TaskOptions { get; }

    /// <summary>模型 id：预设给的候选，也能手输</summary>
    public EditableChoice ModelId { get; } = new();

    /// <summary>音色：预设给的候选（Edge TTS 的从服务取），也能手输；空为服务默认</summary>
    public EditableChoice Voice { get; } = new();

    public string WebsiteUrl => SelectedPreset?.Preset.WebsiteUrl ?? string.Empty;

    public bool HasWebsite => WebsiteUrl.Length > 0;

    /// <summary>Edge TTS：不用地址、密钥、模型</summary>
    public bool IsEdge => SelectedPreset?.Preset.Dialect == EAudioDialect.EdgeTts;

    /// <summary>OpenAI 兼容：要地址、密钥、模型</summary>
    public bool IsCompatible => !IsEdge;

    /// <summary>自定义服务才能选做什么</summary>
    public bool CanChooseTask => SelectedPreset?.Preset.Task == null;

    public bool IsTts => SelectedTask.Task == EAudioCapability.Tts;

    /// <summary>同一服务地址下、填了密钥的对话模型：一键沿用它的密钥</summary>
    public IReadOnlyList<RemoteModelInfo> CopySources => ApiKeySources.Matching(_chatModels, Endpoint);

    public bool HasCopySources => CopySources.Count > 0;

    /// <summary>名字为空或与其他服务重名</summary>
    public bool HasNameError => string.IsNullOrWhiteSpace(Name) ||
                                _takenNames.Contains(Name.Trim(), StringComparer.OrdinalIgnoreCase);

    public bool HasEndpointError => IsCompatible && !ApiKeySources.IsHttpUrl(Endpoint);

    public bool HasModelIdError => IsCompatible && string.IsNullOrWhiteSpace(ModelId.Text);

    public bool CanConfirm => !HasNameError && !HasEndpointError && !HasModelIdError;

    /// <summary>
    /// 按表单产出服务（编辑时保留表单里没有的字段，如超时）
    /// </summary>
    /// <returns>新实例</returns>
    public AudioServiceInfo BuildResult()
    {
        AudioServiceInfo result = _source?.Clone() ?? new AudioServiceInfo();
        AudioServicePreset preset = SelectedPreset?.Preset ?? AudioServicePreset.All[^1];
        result.Name = Name.Trim();
        result.Dialect = preset.Dialect;
        result.Task = preset.Task ?? SelectedTask.Task;
        result.Endpoint = IsEdge ? string.Empty : Endpoint.Trim();
        result.ModelId = IsEdge ? string.Empty : ModelId.Text.Trim();
        result.ApiKey = IsEdge ? string.Empty : ApiKey.Trim();
        result.Voice = result.Task == EAudioCapability.Tts ? Voice.Text.Trim() : string.Empty;
        return result;
    }

    /// <summary>
    /// 关窗：取消在途的试听与拉音色
    /// </summary>
    public void Close() => _lifetime.Cancel();

    /// <summary>
    /// 停下在途的试听（换了服务商、音色、模型，正在念的已经不是表单现在的样子）
    /// </summary>
    public void StopPreview() => _previewing?.Cancel();

    [RelayCommand]
    private void CopyKeyFrom(RemoteModelInfo source) => ApiKey = source.ApiKey;

    // 用表单现在的样子念一句：密钥填错、模型 id 不对，在这里就看得出来。在途时再点一下就停（网络卡住也能停）
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Preview()
    {
        if (_previewing != null)
        {
            StopPreview();
            return;
        }

        if (!IsTts || !CanConfirm) return;
        using CancellationTokenSource session = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _previewing = session;
        IsPreviewing = true;
        try
        {
            await _preview(BuildResult(), session.Token);
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested)
        {
        }
        catch (Exception e) when (AudioErrorText.IsExpected(e))
        {
            _messages.ShowNotification(Loc.Text(LangKey.AudioServicePreviewFailed, AudioErrorText.Describe(e)),
                severity: MessageSeverity.Warning);
        }
        finally
        {
            _previewing = null;
            IsPreviewing = false;
        }
    }

    partial void OnSelectedPresetChanged(AudioServicePresetItem? value)
    {
        StopPreview();
        if (value == null) return;
        if (!IsEditMode)
        {
            AudioServicePreset preset = value.Preset;
            if (preset.Task is { } task) SelectedTask = TaskOptions.First(x => x.Task == task);
            Endpoint = preset.Endpoint;
            ModelId.Text = preset.ModelIds.FirstOrDefault() ?? string.Empty;
            Voice.Text = preset.Voices.FirstOrDefault() ?? string.Empty;
            if (Name.Length == 0 || Name == _autoName) SetAutoName(value.DisplayName);
        }

        LoadOptions(value.Preset);
    }

    partial void OnNameChanged(string value)
    {
        if (value != _autoName) _autoName = string.Empty;
    }

    partial void OnSelectedTaskChanged(AudioTaskOption value) => StopPreview();

    private void OnModelIdChanged(string value)
    {
        StopPreview();
        OnPropertyChanged(nameof(HasModelIdError));
        OnPropertyChanged(nameof(CanConfirm));
    }

    private void SetAutoName(string name)
    {
        string candidate = name;
        for (int i = 2; _takenNames.Contains(candidate, StringComparer.OrdinalIgnoreCase); i++) candidate = $"{name} {i}";
        _autoName = candidate;
        Name = candidate;
    }

    private void LoadOptions(AudioServicePreset preset)
    {
        ModelId.SetOptions(preset.ModelIds);
        Voice.SetOptions(preset.Voices);
        if (preset.Dialect == EAudioDialect.EdgeTts) _ = LoadEdgeVoicesAsync();
    }

    private async Task LoadEdgeVoicesAsync()
    {
        IsLoadingVoices = true;
        try
        {
            IReadOnlyList<string> voices = await _edgeVoices(_lifetime.Token);
            string language = LocalizationManager.Instance.CurrentCulture.TwoLetterISOLanguageName;
            if (IsEdge) Voice.SetOptions(EdgeTtsProvider.SortVoices(voices, language));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            UiharuMind.Core.Core.SimpleLog.Log.Debug($"List Edge TTS voices failed: {e.Message}");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoadingVoices = false;
        }
    }

    private static string PresetName(AudioServicePreset preset)
    {
        if (preset.Key == AudioServicePreset.CustomKey) return Loc.Text(LangKey.AudioServicePresetCustom);
        if (preset.Dialect == EAudioDialect.EdgeTts) return Loc.Text(LangKey.AudioServicePresetEdge);
        string brand = preset.Key.StartsWith("siliconflow", StringComparison.Ordinal)
            ? Loc.Text(LangKey.AudioServiceBrandSiliconFlow)
            : preset.DisplayName;
        return Loc.Text(preset.Task == EAudioCapability.Asr ? LangKey.AudioServicePresetAsr : LangKey.AudioServicePresetTts, brand);
    }
}
