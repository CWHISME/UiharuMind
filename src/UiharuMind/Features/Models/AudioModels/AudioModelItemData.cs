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
    private readonly List<string> _nativeVoices = []; //模型自带的，读到过的都留着
    private IReadOnlyList<string> _libraryVoices = []; //音色库里的，会克隆的模型才列
    private bool _isFilling; //回填音色时不写设置

    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private VoiceOption? _selectedVoice;
    [ObservableProperty] private bool _isLoadingVoices;
    [ObservableProperty] private bool _isPreviewing;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isTuned;

    /// <param name="entry">扫描到的模型</param>
    /// <param name="family">所属家族（预置音色、语种、会不会克隆）；引擎没装、认不出时为 null</param>
    /// <param name="sizeBytes">gguf 文件大小</param>
    /// <param name="settings">语音引擎设置（存音色）</param>
    public AudioModelItemData(AudioCppModelEntry entry, AudioCppFamily? family, long sizeBytes,
        AudioCppSettingConfig settings)
    {
        _settings = settings;
        Languages = family?.Languages ?? [];
        Entry = entry;
        FamilyName = family?.DisplayName ?? entry.Family;
        CanClone = family?.CanClone == true;
        CanDraw = CanClone && family!.BuiltinVoices.Count == 0 && !family.NeedsReference;
        SizeBytes = sizeBytes;
        SizeText = Shared.ModelSizeText.Format(sizeBytes);
        Options = family != null ? AudioCppTunableOptions.For(family) : [];
        _isTuned = settings.ModelOptions.GetValueOrDefault(entry.Id) is { Count: > 0 };
        FillVoices(family?.BuiltinVoices ?? []);
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

    /// <summary>
    /// 会照参考音频克隆：音色库里的音色都能用
    /// </summary>
    public bool CanClone { get; }

    /// <summary>
    /// 能抽卡：会克隆、没有自带音色、不给参考音频也念得出来
    /// </summary>
    public bool CanDraw { get; }

    public string SizeText { get; }

    /// <summary>
    /// gguf 文件大小（字节）
    /// </summary>
    public long SizeBytes { get; }

    /// <summary>
    /// 能调的参数（照模型 spec）；认不出家族时为空
    /// </summary>
    public IReadOnlyList<AudioCppOption> Options { get; }

    public bool HasOptions => Options.Count > 0;

    /// <summary>
    /// 合成模型才有音色
    /// </summary>
    public bool IsTts => Entry.Task == EAudioCppTask.Tts.ToConfigName();

    /// <summary>
    /// 可选音色（第一项是「默认」）
    /// </summary>
    public ObservableCollection<VoiceOption> Voices { get; } = [];

    /// <summary>
    /// 并入从引擎读到的模型自带音色，保留当前选择
    /// </summary>
    /// <param name="voices">音色 id</param>
    public void FillVoices(IEnumerable<string> voices)
    {
        foreach (string voice in voices)
            if (voice.Length > 0 && !_nativeVoices.Contains(voice))
                _nativeVoices.Add(voice);
        RebuildVoices();
    }

    /// <summary>
    /// 换上音色库的现状（会克隆的模型才列出来），保留当前选择
    /// </summary>
    /// <param name="names">音色库里的音色名</param>
    public void SetLibraryVoices(IReadOnlyList<string> names)
    {
        if (!CanClone) return;
        _libraryVoices = names;
        RebuildVoices();
    }

    // 「默认」在前，然后是模型自带的、音色库的；设置里存着却已不在的也列上，免得选择被悄悄换掉
    private void RebuildVoices()
    {
        string? saved = _settings.TtsVoices.GetValueOrDefault(Name);
        List<string> ids = _nativeVoices.Concat(_libraryVoices).Append(saved ?? "")
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
