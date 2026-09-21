using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Memory;

/// <summary>
/// 一个待嵌入的块
/// </summary>
/// <param name="Content">块正文</param>
/// <param name="Context">来源与标题路径,如「手册.md / 安装 / macOS」</param>
public readonly record struct MemoryChunk(string Content, string Context)
{
    /// <summary>
    /// 真正送去嵌入并落库的文本。上下文拼在正文前面——
    /// 文档中段切出来的块本身不含「我属于哪篇、哪一节」,不拼进去向量里就没有这个信息,
    /// 检索时问「macOS 怎么装」就匹配不到标题在上一块里的那段正文。
    /// </summary>
    public string EmbeddingText => Context.Length == 0 ? Content : Context + "\n\n" + Content;
}

/// <summary>
/// 把来源文本切成可嵌入的块。
///
/// 单独成类而不是留在 <see cref="MemoryData"/> 里：切块边界直接决定检索质量,
/// 而它是纯函数——不碰库、不碰嵌入服务、同一段文本任何时候切出的结果逐字一致,
/// 因此可以脱离索引编排单独钉住行为。
///
/// 结构识别不在这里,在 <see cref="MarkdownBlockScanner"/>：那个只认语法,这个只认 token 预算。
/// 本类的活是「拿到块序列,按预算打包成 chunk,单块超预算时在合适的地方下刀」。
///
/// 预算按 token 而非字符算。字符数是坏代理——900 字符英文约 225 token、中文约 600-900 token,
/// 同一个常量在中英文之间差三四倍,结果是英文块只填了四分之一容量,中文块还可能超限。
/// 注意 <see cref="LlmTokenizer"/> 用的是 o200k,与实际嵌入模型的分词器不同,
/// 所以这里算出的是估值,真被端点拒收仍要靠 <see cref="SplitOversized"/> 兜底。
/// </summary>
public static class MemoryTextChunker
{
    /// <summary>单块最大 token 数的兜底值：拿不到嵌入模型上下文长度时用它</summary>
    public const int MaxChunkTokens = 400;

    /// <summary>
    /// 单块 token 上限的封顶。再大对检索没好处——块越大越容易一块里混进好几个主题，
    /// 命中了也带回一堆无关内容，等于把上下文预算浪费在噪音上。
    /// </summary>
    public const int MaxChunkTokensCeiling = 512;

    /// <summary>单块 token 上限的下限。再小就只剩碎片,检索得到也拼不出意思</summary>
    public const int MinChunkTokens = 128;

    /// <summary>
    /// 从嵌入上下文里留出的余量：上下文前缀要占一些，而 o200k 与实际嵌入模型的分词器
    /// 也有出入，贴着上限切必然踩到「被拒 → 拆 → 重发」。
    /// </summary>
    private const int BudgetHeadroomTokens = 64;

    /// <summary>相邻块的重叠 token 数：答案正好落在切口上时,靠重叠让它至少在一块里是完整的</summary>
    public const int ChunkOverlapTokens = 60;

    /// <summary>可继续对半拆的最短长度：再短就没有拆的意义,只会拆出一堆检索不到的碎片</summary>
    public const int MinimumSplitLength = 48;

    /// <summary>就近寻找分割点的最大偏移：超过这个距离仍没有合适边界,就认账从正中间硬切</summary>
    private const int SplitSearchRadius = 160;

    /// <summary>
    /// 句末标点。中文正文没有空白可依,只按空白找切口等于从中间硬切——
    /// 对中文优先的内容这是实打实的质量损失,所以标点排在空白之前。
    /// </summary>
    private static readonly char[] SentenceEnders = ['。', '！', '？', '；', '…', '.', '!', '?', ';'];

    /// <summary>
    /// 按结构与 token 预算切块
    /// </summary>
    /// <param name="text">来源文本,按 Markdown 解读(纯文本是其合法子集)</param>
    /// <param name="sourceName">来源名称,拼进每块的上下文;可为空</param>
    /// <param name="maxTokens">单块最大 token 数</param>
    /// <param name="overlapTokens">相邻块重叠 token 数,必须小于 maxTokens</param>
    /// <returns>切好的块；空白文本返回空序列</returns>
    /// <exception cref="ArgumentOutOfRangeException">maxTokens 非正,或 overlapTokens 不在 [0, maxTokens) 内</exception>
    public static IEnumerable<MemoryChunk> Split(
        string text,
        string sourceName = "",
        int maxTokens = MaxChunkTokens,
        int overlapTokens = ChunkOverlapTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapTokens);
        // 重叠不小于块长会让切窗原地不动:每块都从上一块的开头重新开始,永远推不到文本末尾
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlapTokens, maxTokens);

        return SplitCore(text, sourceName, maxTokens, overlapTokens);
    }

    /// <summary>
    /// 按嵌入模型的上下文长度定单块预算。
    ///
    /// 写死一个常量必然两头不讨好：定小了，8k 上下文的模型被切成一堆碎块，索引慢一倍
    /// 而检索还更差；定大了，512 上下文的模型每块都被端点拒收，全靠拆分兜底。
    /// </summary>
    /// <param name="embeddingContextSize">嵌入模型的上下文长度；非正表示未知</param>
    /// <returns>单块 token 上限，落在 [<see cref="MinChunkTokens"/>, <see cref="MaxChunkTokensCeiling"/>] 内</returns>
    public static int ResolveChunkBudget(int embeddingContextSize)
    {
        if (embeddingContextSize <= 0) return MaxChunkTokens;

        return Math.Clamp(
            embeddingContextSize - BudgetHeadroomTokens, MinChunkTokens, MaxChunkTokensCeiling);
    }

    /// <summary>
    /// 判断文本是否还值得继续拆分
    /// </summary>
    /// <param name="text">待拆分的文本</param>
    /// <returns>长度仍超过 <see cref="MinimumSplitLength"/> 时返回 True</returns>
    public static bool CanSplitFurther(string text) => text.Length > MinimumSplitLength;

    /// <summary>
    /// 把被嵌入服务拒收的超长块对半拆开。tokenizer 因模型而异,按预算切好也可能超限,
    /// 因此这里按实际拒收结果做二次拆分,而不是一开始就把块切得很碎。
    /// </summary>
    /// <param name="text">超长块</param>
    /// <returns>前后两段；就近有句末标点或空白就在那里断开,否则从正中间硬切</returns>
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

    /// <summary>
    /// 累积中的一片内容及其所属标题路径。
    /// 记着各自的路径，是为了在合并跨小节的内容时还能把小节名写回正文。
    /// </summary>
    private readonly record struct Piece(string HeaderPath, string Text, int Tokens);

    private static IEnumerable<MemoryChunk> SplitCore(
        string text, string sourceName, int maxTokens, int overlapTokens)
    {
        int sourceTokens = LlmTokenizer.CountTokens(sourceName);
        List<Piece> pending = [];
        string commonPath = "";
        int bodyTokens = 0;

        foreach (MemoryTextBlock block in MarkdownBlockScanner.Scan(text))
        {
            int contextTokens = sourceTokens + LlmTokenizer.CountTokens(block.HeaderPath);
            int budget = Math.Max(MinimumSplitLength, maxTokens - contextTokens);

            foreach ((string pieceText, int pieceTokens) in SplitBlock(block, budget))
            {
                string mergedPath = pending.Count == 0
                    ? block.HeaderPath
                    : CommonHeaderPrefix(commonPath, block.HeaderPath);

                // 合并跨小节内容时，小节名要写回正文，所以它也占预算
                int inlineTokens = CountInlineHeadings(pending, block.HeaderPath, mergedPath);
                int mergedContextTokens = sourceTokens + LlmTokenizer.CountTokens(mergedPath);
                bool fits = mergedContextTokens + inlineTokens + bodyTokens + pieceTokens <= maxTokens;

                if (pending.Count > 0 && !fits)
                {
                    string emitted = Compose(pending, commonPath);
                    yield return new MemoryChunk(emitted, BuildContext(sourceName, commonPath));

                    pending.Clear();
                    bodyTokens = 0;

                    (string carried, int carriedTokens) = LlmTokenizer.TakeLastTokens(emitted, overlapTokens);
                    if (carried.Length > 0 && carried.Length < emitted.Length)
                    {
                        // 重叠段按新块自己的路径记账：它已经是文字，不该再被当成跨小节内容加标题
                        pending.Add(new Piece(block.HeaderPath, carried, carriedTokens));
                        bodyTokens = carriedTokens;
                    }

                    commonPath = block.HeaderPath;
                }
                else
                {
                    commonPath = mergedPath;
                }

                pending.Add(new Piece(block.HeaderPath, pieceText, pieceTokens));
                bodyTokens += pieceTokens;
            }
        }

        if (pending.Count > 0)
        {
            string tail = Compose(pending, commonPath);
            if (tail.Length > 0) yield return new MemoryChunk(tail, BuildContext(sourceName, commonPath));
        }
    }

    /// <summary>
    /// 把累积的片拼成块正文。路径深于公共前缀的片，前面补一行它自己的小节名——
    /// 否则合并之后就分不清哪段属于哪一节，等于把出处丢了。
    /// </summary>
    private static string Compose(List<Piece> pending, string commonPath)
    {
        StringBuilder builder = StringBuilderPool.Get();
        string lastPath = commonPath;

        foreach (Piece piece in pending)
        {
            if (builder.Length > 0) builder.Append("\n\n");

            if (!string.Equals(piece.HeaderPath, lastPath, StringComparison.Ordinal))
            {
                string suffix = HeaderSuffix(piece.HeaderPath, commonPath);
                if (suffix.Length > 0) builder.Append(suffix).Append('\n');
                lastPath = piece.HeaderPath;
            }

            builder.Append(piece.Text);
        }

        string text = builder.ToString().Trim();
        StringBuilderPool.Release(builder);
        return text;
    }

    /// <summary>把某个路径写回正文时要花的 token；已在公共前缀内则不花</summary>
    private static int CountInlineHeadings(List<Piece> pending, string incomingPath, string mergedPath)
    {
        int tokens = 0;
        string lastPath = mergedPath;

        foreach (Piece piece in pending)
        {
            if (string.Equals(piece.HeaderPath, lastPath, StringComparison.Ordinal)) continue;
            tokens += LlmTokenizer.CountTokens(HeaderSuffix(piece.HeaderPath, mergedPath));
            lastPath = piece.HeaderPath;
        }

        if (!string.Equals(incomingPath, lastPath, StringComparison.Ordinal))
            tokens += LlmTokenizer.CountTokens(HeaderSuffix(incomingPath, mergedPath));

        return tokens;
    }

    /// <summary>两个标题路径按层级取公共前缀。「安装/macOS」与「安装/Windows」的公共前缀是「安装」</summary>
    private static string CommonHeaderPrefix(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal)) return left;
        if (left.Length == 0 || right.Length == 0) return "";

        string[] leftSegments = left.Split(MarkdownBlockScanner.HeaderPathSeparator);
        string[] rightSegments = right.Split(MarkdownBlockScanner.HeaderPathSeparator);

        int shared = 0;
        while (shared < leftSegments.Length && shared < rightSegments.Length &&
               string.Equals(leftSegments[shared], rightSegments[shared], StringComparison.Ordinal))
        {
            shared++;
        }

        return string.Join(MarkdownBlockScanner.HeaderPathSeparator, leftSegments.Take(shared));
    }

    /// <summary>路径超出公共前缀的那一段</summary>
    private static string HeaderSuffix(string path, string commonPath)
    {
        if (commonPath.Length == 0) return path;
        if (!path.StartsWith(commonPath, StringComparison.Ordinal)) return path;
        if (path.Length == commonPath.Length) return "";

        return path[(commonPath.Length + MarkdownBlockScanner.HeaderPathSeparator.Length)..];
    }

    private static string BuildContext(string sourceName, string headerPath)
    {
        if (sourceName.Length == 0) return headerPath;
        return headerPath.Length == 0
            ? sourceName
            : sourceName + MarkdownBlockScanner.HeaderPathSeparator + headerPath;
    }

    /// <summary>
    /// 把一个块切成不超过预算的若干片，连 token 数一起给出。
    ///
    /// 顺带把 token 数带出来，是因为调用方本来就要用：分开算等于对同一段文本
    /// 做两次 BPE 分词，而绝大多数块走的都是「装得下、原样返回」这条路。
    /// </summary>
    private static IEnumerable<(string Text, int Tokens)> SplitBlock(MemoryTextBlock block, int budget)
    {
        int blockTokens = LlmTokenizer.CountTokens(block.Text);
        if (blockTokens <= budget)
        {
            yield return (block.Text, blockTokens);
            yield break;
        }

        string header = block.Kind == EMemoryBlockKind.Table
            ? MarkdownBlockScanner.GetTableHeader(block.Text)
            : "";

        foreach (string piece in SplitByBudget(block.Text, budget, header))
        {
            yield return (piece, LlmTokenizer.CountTokens(piece));
        }
    }

    /// <summary>
    /// 按预算把长文本切成若干片,每片尽量在自然边界断开。
    /// </summary>
    /// <param name="text">长文本</param>
    /// <param name="budget">单片 token 预算</param>
    /// <param name="repeatedHeader">每片都要带上的表头;非表格时为空</param>
    private static IEnumerable<string> SplitByBudget(string text, int budget, string repeatedHeader)
    {
        // 表头要占掉每片的预算,否则带上表头就超了
        int headerTokens = LlmTokenizer.CountTokens(repeatedHeader);
        int bodyBudget = Math.Max(MinimumSplitLength, budget - headerTokens);
        string remaining = text;

        // 表格的第一片已经含表头,不必再重复贴一遍
        bool isFirstPiece = true;

        while (remaining.Length > 0)
        {
            int prefixLength = LlmTokenizer.GetPrefixLengthByTokens(remaining, bodyBudget);
            if (prefixLength >= remaining.Length)
            {
                yield return Decorate(remaining, repeatedHeader, isFirstPiece);
                yield break;
            }

            if (prefixLength <= 0) prefixLength = Math.Min(remaining.Length, MinimumSplitLength);

            int cut = FindBoundaryBefore(remaining, prefixLength);
            string piece = remaining[..cut].TrimEnd();
            if (piece.Length > 0) yield return Decorate(piece, repeatedHeader, isFirstPiece);

            isFirstPiece = false;
            remaining = remaining[cut..].TrimStart();
        }
    }

    private static string Decorate(string piece, string repeatedHeader, bool isFirstPiece)
    {
        if (repeatedHeader.Length == 0 || isFirstPiece) return piece;
        return repeatedHeader + "\n" + piece;
    }

    /// <summary>
    /// 在 limit 之前找一个自然边界:空行 &gt; 换行 &gt; 句末标点 &gt; 空白 &gt; 硬切。
    /// 优先级如此排是因为越靠前的边界越可能是完整语义单元的结尾。
    /// </summary>
    private static int FindBoundaryBefore(string text, int limit)
    {
        ReadOnlySpan<char> window = text.AsSpan(0, limit);

        int blankLine = window.LastIndexOf("\n\n".AsSpan());
        if (blankLine > 0) return blankLine + 2;

        int newLine = window.LastIndexOf('\n');
        if (newLine > 0) return newLine + 1;

        int sentence = window.LastIndexOfAny(SentenceEnders);
        if (sentence > 0) return sentence + 1;

        for (int index = limit - 1; index > 0; index--)
        {
            if (char.IsWhiteSpace(text[index])) return index + 1;
        }

        return limit; //整段没有任何可用边界(典型的无标点中文长句),认账硬切
    }

    private static int FindNearbySplit(string text, int middle)
    {
        int radius = Math.Min(SplitSearchRadius, middle);
        for (int offset = 0; offset < radius; offset++)
        {
            if (TryTakeSplit(text, middle + offset, out int after)) return after;
            if (TryTakeSplit(text, middle - offset, out int before)) return before;
        }

        return middle;
    }

    /// <summary>标点要切在它后面,空白切在它这里——前者属于上一句,后者只是分隔</summary>
    private static bool TryTakeSplit(string text, int candidate, out int split)
    {
        split = 0;
        if (candidate <= 0 || candidate >= text.Length) return false;

        if (Array.IndexOf(SentenceEnders, text[candidate]) >= 0)
        {
            split = candidate + 1;
            return true;
        }

        if (char.IsWhiteSpace(text[candidate]))
        {
            split = candidate;
            return true;
        }

        return false;
    }
}
