namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// model_specs 里的一个模型家族：说明、能力与它的各个 GGUF 包
/// </summary>
/// <param name="Family">家族 id，如 pocket_tts</param>
/// <param name="Task">tts / asr</param>
/// <param name="DisplayName">显示名</param>
/// <param name="Description">上游说明（英文原文）</param>
/// <param name="Status">上游成熟度：supported / experimental / community / wip…</param>
/// <param name="Languages">语种</param>
/// <param name="Tasks">能做的事：tts、clone、design、asr、align…</param>
/// <param name="Modes">offline / streaming</param>
/// <param name="Capabilities">能力细项（各任务下的去重合并），如 word_timestamps、long_form</param>
/// <param name="BuiltinVoices">预置音色</param>
/// <param name="RecommendedPackage">上游推荐的包 id；没给为空</param>
/// <param name="NeedsEspeak">要系统装 eSpeak-ng</param>
/// <param name="Packages">能下的 GGUF 包</param>
/// <param name="RequestOptions">合成 / 识别请求能带的参数名（spec 的 options.request），如 speed、seed、temperature</param>
public sealed record AudioCppFamily(
    string Family,
    EAudioCppTask Task,
    string DisplayName,
    string Description,
    string Status,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Tasks,
    IReadOnlyList<string> Modes,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> BuiltinVoices,
    string RecommendedPackage,
    bool NeedsEspeak,
    IReadOnlyList<AudioCppPackage> Packages,
    IReadOnlyList<string> RequestOptions)
{
    /// <summary>
    /// 合成时能变语速（不能的，server 收到非 1 的 speed 会报错）
    /// </summary>
    public bool AcceptsSpeed => RequestOptions.Contains("speed") || RequestOptions.Contains("speaking_rate");

    /// <summary>
    /// 会照参考音频克隆音色（能用音色库）
    /// </summary>
    public bool CanClone => Tasks.Contains("clone");

    /// <summary>
    /// 只会克隆：不给参考音频就念不了
    /// </summary>
    public bool NeedsReference => CanClone && !Tasks.Contains("tts");

    /// <summary>
    /// 家族里有本应用推荐的包
    /// </summary>
    public bool IsRecommended => Packages.Any(x => AudioCppModelCatalog.Recommended.Contains(x.Id));

    /// <summary>
    /// 家族的默认包：本应用推荐的 → 上游推荐的 → 标了 default 的 → 第一个
    /// </summary>
    public AudioCppPackage? DefaultPackage =>
        Packages.FirstOrDefault(x => AudioCppModelCatalog.Recommended.Contains(x.Id)) ??
        Packages.FirstOrDefault(x => x.Id == RecommendedPackage) ??
        Packages.FirstOrDefault(x => x.IsDefault) ??
        Packages.FirstOrDefault();
}
