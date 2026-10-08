namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 模型参数里哪些给用户调：照 spec 通用生成，只按参数名（不按模型）挑出常用的、藏起别处已经管着的
/// </summary>
public static class AudioCppTunableOptions
{
    // 别处已经管着的：语速、语种、语气、参考音频这些归角色声音与音色库；其余是给程序用的
    private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal)
    {
        "speed", "speaking_rate", "language", "lang", "instruction", "instruct", "voice_id",
        "reference_text", "prompt_text", "reference_language", "multi_reference_cond", "voice_samples",
        "voice_clone_text", "source_text", "target_text", "phonemes", "return_timestamps", "return_video", "streaming"
    };

    // 常用的参数，按这个顺序排在前面；名字在各家模型里含义一致，界面据此给中文名与说明
    private static readonly string[] CommonOrder =
    [
        "seed", "temperature", "top_p", "top_k", "num_inference_steps", "guidance_scale", "cfg_strength",
        "repetition_penalty", "max_tokens", "text_chunk_size", "num_beams", "context", "hotwords"
    ];

    /// <summary>
    /// 常用的参数名
    /// </summary>
    public static IReadOnlyList<string> Common => CommonOrder;

    /// <summary>
    /// 某个家族给用户调的参数：常用的在前（按 <see cref="Common"/> 的顺序），其余照 spec 的顺序
    /// </summary>
    /// <param name="family">模型家族</param>
    /// <returns>参数</returns>
    public static IReadOnlyList<AudioCppOption> For(AudioCppFamily family) => family.RequestOptions
        .Where(IsTunable)
        .OrderBy(x => Array.IndexOf(CommonOrder, x.Name) is var index and >= 0 ? index : CommonOrder.Length)
        .ToList();

    /// <summary>
    /// 是常用参数
    /// </summary>
    /// <param name="name">参数名</param>
    /// <returns>是为 true</returns>
    public static bool IsCommon(string name) => CommonOrder.Contains(name);

    private static bool IsTunable(AudioCppOption option) =>
        option.Type != EAudioCppOptionType.Other && !Hidden.Contains(option.Name) &&
        !option.Name.StartsWith("stream", StringComparison.Ordinal) &&
        !option.Name.EndsWith("_path", StringComparison.Ordinal);
}
