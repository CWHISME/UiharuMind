using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Audio;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.AudioModels.Online;

/// <summary>
/// 「语音模型」页签里的一个在线服务
/// </summary>
public partial class AudioServiceItemData : ObservableObject
{
    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private bool _isPreviewing;

    /// <param name="info">服务条目</param>
    public AudioServiceItemData(AudioServiceInfo info)
    {
        Info = info;
        string voice = info.Dialect == EAudioDialect.EdgeTts && info.Voice.Length == 0
            ? Loc.Text(LangKey.AudioServiceVoiceAuto)
            : info.Voice;
        string provider = info.Dialect == EAudioDialect.EdgeTts ? "Edge TTS" : HostOf(info.Endpoint);
        Detail = string.Join("  ·  ", new[] { provider, info.ModelId, voice }.Where(x => x.Length > 0));
    }

    public AudioServiceInfo Info { get; }

    public string Name => Info.Name;

    /// <summary>
    /// 形如「api.openai.com · gpt-4o-mini-tts · alloy」
    /// </summary>
    public string Detail { get; }

    public bool IsTts => Info.Task == EAudioCapability.Tts;

    /// <summary>
    /// 没填全（如缺地址），用不了
    /// </summary>
    public bool IsIncomplete => !Info.IsConfigured;

    private static string HostOf(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ? uri.Host : endpoint;
}

/// <summary>
/// 在线语音服务：增删改、设为朗读或听写的默认、试听。和本地模型排在同一个分组里，默认二选一，由 <see cref="AudioRouter"/> 记
/// </summary>
public partial class AudioServiceListViewData : ObservableObject
{
    private readonly AudioServiceSettingConfig _config;
    private readonly IReadAloudService _readAloud;
    private readonly IMessageService _messages;
    private readonly Func<AudioServiceInfo?, IReadOnlyCollection<string>, Task<AudioServiceInfo?>> _openEditor;

    /// <param name="router">朗读、听写各用哪个</param>
    /// <param name="config">服务列表</param>
    /// <param name="readAloud">试听（与消息朗读同一路）</param>
    /// <param name="messages">确认与提示</param>
    /// <param name="openEditor">打开编辑框：待编辑的服务（null 为新建）、其他服务已用的名字 → 确认后的新实例</param>
    public AudioServiceListViewData(AudioRouter router, AudioServiceSettingConfig config, IReadAloudService readAloud,
        IMessageService messages, Func<AudioServiceInfo?, IReadOnlyCollection<string>, Task<AudioServiceInfo?>> openEditor)
    {
        Router = router;
        _config = config;
        _readAloud = readAloud;
        _messages = messages;
        _openEditor = openEditor;
        router.DefaultsChanged += RefreshDefaults;
        readAloud.StateChanged += OnReadAloudChanged;
        Refresh();
    }

    /// <summary>
    /// 朗读、听写各用哪个
    /// </summary>
    public AudioRouter Router { get; }

    /// <summary>
    /// 朗读用的在线服务
    /// </summary>
    public ObservableCollection<AudioServiceItemData> TtsServices { get; } = [];

    /// <summary>
    /// 听写用的在线服务
    /// </summary>
    public ObservableCollection<AudioServiceItemData> AsrServices { get; } = [];

    /// <summary>
    /// 列表或默认变了（本地模型那边据此重算「默认」标记与分组显隐）
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// 按配置重建列表
    /// </summary>
    public void Refresh()
    {
        TtsServices.Clear();
        AsrServices.Clear();
        foreach (AudioServiceInfo info in _config.Services)
            (info.Task == EAudioCapability.Tts ? TtsServices : AsrServices).Add(new AudioServiceItemData(info));
        RefreshDefaults();
    }

    [RelayCommand]
    private async Task Add()
    {
        AudioServiceInfo? created = await _openEditor(null, NamesExcept(null));
        if (created == null) return;
        Commit(services => services.Add(created));
    }

    [RelayCommand]
    private async Task Edit(AudioServiceItemData item)
    {
        AudioServiceInfo? edited = await _openEditor(item.Info, NamesExcept(item.Info));
        int index = _config.Services.IndexOf(item.Info);
        if (edited == null || index < 0) return;
        Commit(services => services[index] = edited);
        if (edited.Name != item.Info.Name || edited.Task != item.Info.Task) Router.OnServiceRenamed(item.Info.Name, edited);
    }

    [RelayCommand]
    private async Task Delete(AudioServiceItemData item)
    {
        if (!await _messages.ConfirmAsync(Loc.Text(LangKey.AudioServiceDeleteConfirm, item.Name))) return;
        Commit(services => services.Remove(item.Info));
    }

    [RelayCommand]
    private void SetDefault(AudioServiceItemData item)
    {
        Router.UseService(item.Info);
        _messages.ShowNotification(Loc.Text(item.IsTts ? LangKey.AudioDefaultSetTts : LangKey.AudioDefaultSetAsr, item.Name),
            severity: MessageSeverity.Success);
    }

    // 再点一下停；与消息朗读、本地模型试听互相打断。失败的提示由朗读服务出
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task Preview(AudioServiceItemData item) =>
        _readAloud.ToggleAsync(item, Loc.Text(LangKey.AudioPreviewText), new SpeechStyle { Provider = item.Info.Id },
            preview: true);

    private void Commit(Action<List<AudioServiceInfo>> change)
    {
        List<AudioServiceInfo> services = _config.Services.ToList();
        change(services);
        _config.ReplaceServices(services);
        Refresh();
    }

    private void RefreshDefaults()
    {
        AudioServiceInfo? tts = Router.DefaultService(EAudioCapability.Tts);
        AudioServiceInfo? asr = Router.DefaultService(EAudioCapability.Asr);
        foreach (AudioServiceItemData item in TtsServices) item.IsDefault = ReferenceEquals(item.Info, tts);
        foreach (AudioServiceItemData item in AsrServices) item.IsDefault = ReferenceEquals(item.Info, asr);
        Changed?.Invoke();
    }

    private void OnReadAloudChanged()
    {
        foreach (AudioServiceItemData item in TtsServices)
            item.IsPreviewing = ReferenceEquals(_readAloud.Owner, item) && _readAloud.State != EReadAloudState.Idle;
    }

    private IReadOnlyCollection<string> NamesExcept(AudioServiceInfo? self) =>
        _config.Services.Where(x => !ReferenceEquals(x, self)).Select(x => x.Name).ToList();
}
