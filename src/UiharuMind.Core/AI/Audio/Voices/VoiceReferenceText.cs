namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 生成或录制参考音频时念的句子：七八秒长、语气平稳，克隆效果最好。
/// 不给参考音频时，VoxCPM 这类模型的说话人随文字内容走（实测中文句多出女声、英文句多出男声），
/// 所以抽卡时句子与种子一起换，抽到的声音才够多样
/// </summary>
public static class VoiceReferenceText
{
    /// <summary>
    /// 中文参考句
    /// </summary>
    public static readonly IReadOnlyList<string> Chinese =
    [
        "今天的天气很不错，我们一起出去走走吧。路边的花开得正好，空气里有淡淡的香味。",
        "我刚看完一本很有意思的书，讲的是一个人独自穿越沙漠的故事，结局让人想了很久。",
        "下午三点的会议改到明天上午了，请大家提前把材料准备好，有问题随时联系我。"
    ];

    /// <summary>
    /// 英文参考句
    /// </summary>
    public static readonly IReadOnlyList<string> English =
    [
        "The weather is lovely today, so let's take a walk outside. The flowers along the road are in full bloom.",
        "I just finished a fascinating book about a man who crossed the desert alone, and the ending stayed with me.",
        "The afternoon meeting has moved to tomorrow morning, so please have your notes ready before then."
    ];

    /// <summary>
    /// 按模型会的语种挑一句：会中文念中文，否则念英文
    /// </summary>
    /// <param name="languages">模型的语种代码；空当作都会</param>
    /// <param name="preferred">想要的语种（如界面语言 zh）；null 优先中文</param>
    /// <returns>参考句</returns>
    public static string For(IReadOnlyList<string> languages, string? preferred = null)
    {
        bool wantsChinese = preferred == null || preferred.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        return SpeaksChinese(languages) && wantsChinese ? Chinese[0] : English[0];
    }

    /// <summary>
    /// 抽卡用的一句：模型会中文就在中英文里随种子挑，否则只在英文里挑
    /// </summary>
    /// <param name="languages">模型的语种代码；空当作都会</param>
    /// <param name="seed">随机种子</param>
    /// <returns>参考句</returns>
    public static string ForDraw(IReadOnlyList<string> languages, int seed)
    {
        IReadOnlyList<string> pool = SpeaksChinese(languages) ? [.. Chinese, .. English] : English;
        return pool[(int)((uint)seed % (uint)pool.Count)];
    }

    private static bool SpeaksChinese(IReadOnlyList<string> languages) =>
        languages.Count == 0 || languages.Any(x => x.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
}
