using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Audio;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.AudioModels.Voices;

/// <summary>
/// 音色库里的一个音色
/// </summary>
public partial class VoiceItemData : ObservableObject
{
    [ObservableProperty] private bool _isPlaying;

    /// <param name="sample">音色</param>
    /// <param name="durationText">时长文案</param>
    public VoiceItemData(VoiceSample sample, string durationText)
    {
        Sample = sample;
        DurationText = durationText;
    }

    public VoiceSample Sample { get; }

    public string Name => Sample.Name;

    public string Transcript => Sample.Transcript;

    public string DurationText { get; }
}

/// <summary>
/// 「语音模型」页签里的音色库：会克隆的模型都能用这里的音色。可试听、删除，或抽卡、录音、导入新的
/// </summary>
public partial class VoiceLibraryViewData : ObservableObject
{
    private readonly VoiceLibrary _library;
    private readonly AudioCppSettingConfig _settings;
    private readonly IMessageService _messages;
    private readonly VoiceClipPlayer _clips;
    private readonly Func<Task<VoiceSample?>> _openCreator;
    private readonly Action<string> _openFolder;

    /// <param name="library">音色库</param>
    /// <param name="settings">语音引擎设置（删音色时清掉各模型对它的选择）</param>
    /// <param name="messages">确认与提示</param>
    /// <param name="player">试听</param>
    /// <param name="openCreator">打开「添加音色」→ 存好的音色，取消为 null</param>
    /// <param name="openFolder">打开目录</param>
    /// <param name="post">切回界面线程（音色可能在后台生成）</param>
    public VoiceLibraryViewData(VoiceLibrary library, AudioCppSettingConfig settings, IMessageService messages,
        IAudioPlayer player, Func<Task<VoiceSample?>> openCreator, Action<string> openFolder, Action<Action> post)
    {
        _library = library;
        _settings = settings;
        _messages = messages;
        _clips = new VoiceClipPlayer(player);
        _clips.Changed += OnClipsChanged;
        _openCreator = openCreator;
        _openFolder = openFolder;
        library.Changed += () => post(Refresh);
    }

    /// <summary>
    /// 音色，按名字排
    /// </summary>
    public ObservableCollection<VoiceItemData> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    /// <summary>
    /// 音色名
    /// </summary>
    public IReadOnlyList<string> Names => Items.Select(x => x.Name).ToList();

    /// <summary>
    /// 重读了音色库
    /// </summary>
    public event Action? Refreshed;

    /// <summary>
    /// 重读音色库
    /// </summary>
    public void Refresh()
    {
        Items.Clear();
        foreach (VoiceSample sample in _library.List()) Items.Add(new VoiceItemData(sample, ReadDuration(sample.Path)));
        OnPropertyChanged(nameof(HasItems));
        Refreshed?.Invoke();
    }

    [RelayCommand]
    private async Task Add()
    {
        _clips.Stop();
        VoiceSample? sample = await _openCreator();
        if (sample == null) return;
        Refresh();
        _messages.ShowNotification(Loc.Text(LangKey.VoiceSaved, sample.Name), severity: MessageSeverity.Success);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Play(VoiceItemData item)
    {
        try
        {
            byte[] wav = await File.ReadAllBytesAsync(item.Sample.Path);
            await _clips.ToggleAsync(item, wav);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException || AudioErrorText.IsExpected(e))
        {
            _messages.ShowNotification(e is IOException or UnauthorizedAccessException ? e.Message : AudioErrorText.Describe(e),
                severity: MessageSeverity.Error);
        }
    }

    // 删掉后，选了它的模型回到「默认」
    [RelayCommand]
    private async Task Delete(VoiceItemData item)
    {
        if (!await _messages.ConfirmAsync(Loc.Text(LangKey.VoiceDeleteConfirm, item.Name))) return;
        if (ReferenceEquals(_clips.Owner, item)) _clips.Stop();
        try
        {
            _library.Delete(item.Name);
        }
        catch (IOException e)
        {
            _messages.ShowNotification(e.Message, severity: MessageSeverity.Error);
            return;
        }

        List<string> users = _settings.TtsVoices.Where(x => x.Value == item.Name).Select(x => x.Key).ToList();
        foreach (string model in users) _settings.TtsVoices.Remove(model);
        if (users.Count > 0) _settings.Save();
        Refresh();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(_library.Directory);
        _openFolder(_library.Directory);
    }

    private void OnClipsChanged()
    {
        foreach (VoiceItemData item in Items) item.IsPlaying = ReferenceEquals(_clips.Owner, item);
    }

    // 参考音频只有几秒，整个读进来算
    private static string ReadDuration(string path)
    {
        try
        {
            return VoiceCreateViewData.FormatDuration(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
