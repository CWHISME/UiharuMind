namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 一个在线语音服务的预设：新建时选它，地址、模型、音色候选就填好了（照 ImageModelPreset）
/// </summary>
/// <param name="Key">稳定标识</param>
/// <param name="DisplayName">显示名（品牌名原样；自定义那项由界面本地化）</param>
/// <param name="Dialect">接口格式</param>
/// <param name="Task">做什么；null = 自定义，两样都能选</param>
/// <param name="Endpoint">服务根地址</param>
/// <param name="ModelIds">模型候选，第一个为默认</param>
/// <param name="Voices">音色候选，第一个为默认；Edge TTS 的从服务取</param>
/// <param name="WebsiteUrl">去哪申请密钥</param>
/// <param name="NeedsKey">要填密钥</param>
public sealed record AudioServicePreset(
    string Key,
    string DisplayName,
    EAudioDialect Dialect,
    EAudioCapability? Task,
    string Endpoint,
    IReadOnlyList<string> ModelIds,
    IReadOnlyList<string> Voices,
    string WebsiteUrl = "",
    bool NeedsKey = true)
{
    /// <summary>自定义 OpenAI 兼容服务</summary>
    public const string CustomKey = "custom";

    private const string CosyVoice = "FunAudioLLM/CosyVoice2-0.5B";

    /// <summary>全部预设，自定义在最后</summary>
    public static IReadOnlyList<AudioServicePreset> All { get; } =
    [
        new("edge", "Edge TTS", EAudioDialect.EdgeTts, EAudioCapability.Tts, "", [], [], NeedsKey: false),
        new("openai-tts", "OpenAI", EAudioDialect.OpenAICompatible, EAudioCapability.Tts, "https://api.openai.com/v1",
            ["gpt-4o-mini-tts", "tts-1", "tts-1-hd"],
            ["alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer"],
            "https://platform.openai.com"),
        new("openai-asr", "OpenAI", EAudioDialect.OpenAICompatible, EAudioCapability.Asr, "https://api.openai.com/v1",
            ["gpt-4o-mini-transcribe", "gpt-4o-transcribe", "whisper-1"], [], "https://platform.openai.com"),
        new("siliconflow-tts", "SiliconFlow", EAudioDialect.OpenAICompatible, EAudioCapability.Tts,
            "https://api.siliconflow.cn/v1", [CosyVoice],
            [.. new[] { "anna", "bella", "claire", "diana", "alex", "benjamin", "charles", "david" }.Select(x => $"{CosyVoice}:{x}")],
            "https://cloud.siliconflow.cn"),
        new("siliconflow-asr", "SiliconFlow", EAudioDialect.OpenAICompatible, EAudioCapability.Asr,
            "https://api.siliconflow.cn/v1", ["FunAudioLLM/SenseVoiceSmall"], [], "https://cloud.siliconflow.cn"),
        new(CustomKey, "Custom", EAudioDialect.OpenAICompatible, null, "", [], [])
    ];

    /// <summary>
    /// 某个条目最像哪个预设（编辑旧条目时选中它）：同格式、同任务、同地址，找不到算自定义
    /// </summary>
    /// <param name="service">服务</param>
    /// <returns>预设</returns>
    public static AudioServicePreset Match(AudioServiceInfo service) =>
        All.FirstOrDefault(x => x.Dialect == service.Dialect && x.Task == service.Task &&
                                (x.Dialect == EAudioDialect.EdgeTts ||
                                 string.Equals(x.Endpoint.TrimEnd('/'), service.Endpoint.TrimEnd('/'),
                                     StringComparison.OrdinalIgnoreCase)))
        ?? All[^1];
}
