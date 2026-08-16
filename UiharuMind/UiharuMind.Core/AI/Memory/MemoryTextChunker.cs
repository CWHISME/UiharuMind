namespace UiharuMind.Core.AI.Memory;

/// <summary>
/// 把来源文本切成可嵌入的块。
///
/// 单独成类而不是留在 <see cref="MemoryData"/> 里：切块边界直接决定检索质量，
/// 而它是纯函数——不碰库、不碰嵌入服务、同一段文本任何时候切出的结果逐字一致，
/// 因此可以脱离索引编排单独钉住行为。三个调参值都在这里，不再散在编排代码中间。
/// </summary>
public static class MemoryTextChunker
{
    /// <summary>单块最大字符数：给常见嵌入模型的上下文留足余量，过大就要靠拒绝后拆分兜底</summary>
    public const int MaxChunkLength = 900;

    /// <summary>相邻块的重叠字符数：答案正好落在切口上时，靠重叠让它至少在一块里是完整的</summary>
    public const int ChunkOverlap = 120;

    /// <summary>可继续对半拆的最短长度：再短就没有拆的意义，只会拆出一堆检索不到的碎片</summary>
    public const int MinimumSplitLength = 48;

    /// <summary>就近寻找空白分割点的最大偏移：超过这个距离仍没有空白，就认账从正中间硬切</summary>
    private const int SplitSearchRadius = 160;

    /// <summary>
    /// 按最大长度加重叠切块
    /// </summary>
    /// <param name="text">来源文本，换行会先归一成 \n 并去掉首尾空白</param>
    /// <param name="maxLength">单块最大字符数</param>
    /// <param name="overlap">相邻块的重叠字符数</param>
    /// <returns>切好的块；空白文本返回空序列</returns>
    public static IEnumerable<string> Split(
        string text, int maxLength = MaxChunkLength, int overlap = ChunkOverlap)
    {
        string normalized = text.Replace("\r\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(normalized)) yield break;

        int start = 0;
        while (start < normalized.Length)
        {
            int length = Math.Min(maxLength, normalized.Length - start);
            yield return normalized.Substring(start, length);
            if (start + length >= normalized.Length) break;
            start += Math.Max(1, maxLength - overlap);
        }
    }

    /// <summary>
    /// 判断文本是否还值得继续拆分
    /// </summary>
    /// <param name="text">待拆分的文本</param>
    /// <returns>长度仍超过 <see cref="MinimumSplitLength"/> 时返回 True</returns>
    public static bool CanSplitFurther(string text) => text.Length > MinimumSplitLength;

    /// <summary>
    /// 把被嵌入服务拒收的超长块对半拆开。tokenizer 因模型而异，长度切好也可能超限，
    /// 因此这里按实际拒收结果做二次拆分，而不是一开始就把块切得很碎。
    /// </summary>
    /// <param name="text">超长块</param>
    /// <returns>前后两段；就近有空白就在空白处断开，否则从正中间硬切</returns>
    public static (string First, string Second) SplitOversized(string text)
    {
        int middle = text.Length / 2;
        int split = FindNearbySplit(text, middle);
        string first = text[..split].Trim();
        string second = text[split..].Trim();
        if (first.Length == 0 || second.Length == 0)
        {
            first = text[..middle];
            second = text[middle..];
        }

        return (first, second);
    }

    private static int FindNearbySplit(string text, int middle)
    {
        for (int offset = 0; offset < Math.Min(SplitSearchRadius, middle); offset++)
        {
            int after = middle + offset;
            if (after < text.Length && char.IsWhiteSpace(text[after])) return after;

            int before = middle - offset;
            if (before > 0 && char.IsWhiteSpace(text[before])) return before;
        }

        return middle;
    }
}
