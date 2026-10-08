using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.AudioModels;

/// <summary>
/// 一个可选音色；Id 为空表示交给模型默认（服务列出的第一个）
/// </summary>
/// <param name="Id">音色 id</param>
/// <param name="DisplayName">显示名</param>
public sealed record VoiceOption(string Id, string DisplayName);

/// <summary>
/// 语音模型页签里的一个本地模型
/// </summary>
public partial class AudioModelItemData : ObservableObject
{
    private const string EnglishPreviewText = "Hello, this is a preview of this voice model.";

    private readonly AudioCppSettingConfig _settings;
    private bool _isFilling; //回填音色时不写设置

    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private VoiceOption? _selectedVoice;
    [ObservableProperty] private bool _isLoadingVoices;
    [ObservableProperty] private bool _isPreviewing;

    /// <param name="entry">扫描到的模型</param>
    /// <param name="familyName">家族显示名；引擎没装、认不出时用家族 id</param>
    /// <param name="sizeBytes">gguf 文件大小</param>
    /// <param name="builtinVoices">spec 里的预置音色（不必启动引擎就能选）</param>
    /// <param name="languages">spec 里的语种（挑试听的句子）；引擎没装时为空</param>
    /// <param name="settings">语音引擎设置（存音色）</param>
    public AudioModelItemData(AudioCppModelEntry entry, string familyName, long sizeBytes,
        IReadOnlyList<string> builtinVoices, IReadOnlyList<string> languages, AudioCppSettingConfig settings)
    {
        _settings = settings;
        Languages = languages;
        Entry = entry;
        FamilyName = familyName;
        SizeText = sizeBytes > 0 ? UiharuMind.Core.Core.Utils.GameUtils.FormatBytes(sizeBytes) : "";
        FillVoices(builtinVoices);
    }

    /// <summary>
    /// 扫描到的模型
    /// </summary>
    public AudioCppModelEntry Entry { get; }

    public string Name => Entry.Id;

    /// <summary>
    /// 语种代码
    /// </summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>
    /// 试听念的句子：模型会说界面语言就用界面语言，否则念英文（大多数模型都会）
    /// </summary>
    public string PreviewText
    {
        get
        {
            string language = LocalizationManager.Instance.CurrentCulture.TwoLetterISOLanguageName;
            return Languages.Any(x => x.StartsWith(language, StringComparison.OrdinalIgnoreCase))
                ? Loc.Text(LangKey.AudioPreviewText)
                : EnglishPreviewText;
        }
    }

    public string FamilyName { get; }

    public string SizeText { get; }

    /// <summary>
    /// 合成模型才有音色
    /// </summary>
    public bool IsTts => Entry.Task == EAudioCppTask.Tts.ToConfigName();

    /// <summary>
    /// 可选音色（第一项是「默认」）
    /// </summary>
    public ObservableCollection<VoiceOption> Voices { get; } = [];

    /// <summary>
    /// 并入从引擎读到的音色，保留当前选择
    /// </summary>
    /// <param name="voices">音色 id</param>
    public void FillVoices(IEnumerable<string> voices)
    {
        string? saved = _settings.TtsVoices.GetValueOrDefault(Name);
        List<string> ids = Voices.Skip(1).Select(x => x.Id).Concat(voices).Append(saved ?? "")
            .Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        _isFilling = true;
        Voices.Clear();
        Voices.Add(new VoiceOption("", Loc.Text(LangKey.AudioVoiceDefault)));
        foreach (string id in ids) Voices.Add(new VoiceOption(id, id));
        SelectedVoice = Voices.FirstOrDefault(x => x.Id == (saved ?? "")) ?? Voices[0];
        _isFilling = false;
    }

    partial void OnSelectedVoiceChanged(VoiceOption? value)
    {
        if (_isFilling || value == null) return;
        if (value.Id.Length == 0) _settings.TtsVoices.Remove(Name);
        else _settings.TtsVoices[Name] = value.Id;
        _settings.Save();
    }
}
