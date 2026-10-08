using System;
using System.Collections.Generic;
using System.Linq;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads.Audio;

/// <summary>
/// 语音模型 spec 字段的界面文案：任务、成熟度、能力标签、语种、包的短名
/// </summary>
public static class AudioModelText
{
    private const int MaxVoicesShown = 6;

    // spec 里的任务、模式与能力细项 → 标签；同一个意思的多个写法收成一条，没收录的不显示（免得中文界面夹英文）
    private static readonly (string Key, LangKey LangKey)[] FeatureKeys =
    [
        ("clone", LangKey.AudioCapClone),
        ("speaker_reference", LangKey.AudioCapClone),
        ("design", LangKey.AudioCapDesign),
        ("voice_design", LangKey.AudioCapDesign),
        ("streaming", LangKey.AudioCapStreaming),
        ("partial_results", LangKey.AudioCapPartial),
        ("long_form", LangKey.AudioCapLongForm),
        ("word_timestamps", LangKey.AudioCapTimestamps),
        ("segments", LangKey.AudioCapTimestamps),
        ("vad_chunking", LangKey.AudioCapVad),
        ("speaker_turns", LangKey.AudioCapSpeakers),
        ("multi_speaker", LangKey.AudioCapMultiSpeaker),
        ("style_control", LangKey.AudioCapStyle),
        ("emotion_control", LangKey.AudioCapEmotion),
        ("edit", LangKey.AudioCapEdit),
        ("prompt_editing", LangKey.AudioCapEdit),
        ("vc", LangKey.AudioCapVoiceConversion),
        ("s2s", LangKey.AudioCapSpeechToSpeech),
        ("align", LangKey.AudioCapAlign)
    ];

    /// <summary>
    /// 合成 / 识别
    /// </summary>
    /// <param name="task">任务</param>
    /// <returns>文案</returns>
    public static string TaskText(EAudioCppTask task) =>
        Loc.Text(task == EAudioCppTask.Tts ? LangKey.AudioTaskTts : LangKey.AudioTaskAsr);

    /// <summary>
    /// 上游成熟度：supported 不标，认得的翻成中文，其余原样
    /// </summary>
    /// <param name="status">spec 的 status</param>
    /// <returns>文案；不必标为 null</returns>
    public static string? MaturityText(string status) => status switch
    {
        "" or "supported" => null,
        "experimental" => Loc.Text(LangKey.AudioMaturityExperimental),
        "community" => Loc.Text(LangKey.AudioMaturityCommunity),
        "testing" => Loc.Text(LangKey.AudioMaturityTesting),
        "wip" => Loc.Text(LangKey.AudioMaturityWip),
        _ => status
    };

    /// <summary>
    /// 家族的能力标签（克隆、流式、时间戳……），按收录顺序、去重
    /// </summary>
    /// <param name="family">家族</param>
    /// <returns>标签</returns>
    public static IReadOnlyList<string> FeatureTags(AudioCppFamily family)
    {
        HashSet<string> keys = new(family.Tasks.Concat(family.Modes).Concat(family.Capabilities), StringComparer.Ordinal);
        List<string> tags = FeatureKeys.Where(x => keys.Contains(x.Key)).Select(x => Loc.Text(x.LangKey)).Distinct().ToList();
        // 主类之外还会另一件：放最前面。只是标出来——同一个模型当两用要在引擎里登记两次、加载两份
        if (family.Task == EAudioCppTask.Tts && keys.Contains("asr")) tags.Insert(0, Loc.Text(LangKey.AudioCapAlsoAsr));
        if (family.Task == EAudioCppTask.Asr && keys.Contains("tts")) tags.Insert(0, Loc.Text(LangKey.AudioCapAlsoTts));
        return tags;
    }

    /// <summary>
    /// 语种：全部列出（详情栏会换行）
    /// </summary>
    /// <param name="languages">语种代码</param>
    /// <returns>文案；没有为空</returns>
    public static string LanguagesText(IReadOnlyList<string> languages) =>
        languages.Count == 0 ? "" : Loc.Text(LangKey.AudioFamilyLanguages, string.Join(", ", languages));

    /// <summary>
    /// 预置音色：过多时只列前几个，带总数（Kokoro 有五十多个，全文放悬停提示）
    /// </summary>
    /// <param name="voices">音色 id</param>
    /// <returns>文案；没有为 null</returns>
    public static string? VoicesText(IReadOnlyList<string> voices)
    {
        if (voices.Count == 0) return null;
        string shown = string.Join(", ", voices.Take(MaxVoicesShown));
        return Loc.Text(LangKey.AudioFamilyVoices, voices.Count <= MaxVoicesShown
            ? shown
            : Loc.Text(LangKey.AudioFamilyVoicesMore, shown, voices.Count));
    }

    /// <summary>
    /// 包在家族详情里的短名：去掉打头的家族名与「GGUF」字样，如 PocketTTS English Q8_0 GGUF → English Q8_0
    /// </summary>
    /// <param name="package">包</param>
    /// <param name="familyName">家族显示名</param>
    /// <returns>短名；去完为空时用原名，没有显示名时用包 id</returns>
    public static string PackageLabel(AudioCppPackage package, string familyName)
    {
        if (package.DisplayName.Length == 0) return package.Id;
        string name = package.DisplayName;
        // 家族名后面得跟空格才算打头，免得把 Audio8-ASR-0.1B 这类名字切进单词中间
        string label = name.StartsWith(familyName + " ", StringComparison.OrdinalIgnoreCase) ? name[familyName.Length..] : name;
        label = string.Join(' ', label.Replace("(GGUF)", "", StringComparison.OrdinalIgnoreCase)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.Equals("GGUF", StringComparison.OrdinalIgnoreCase)));
        return label.Length > 0 ? label : name;
    }
}
