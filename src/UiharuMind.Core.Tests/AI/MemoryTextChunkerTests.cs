using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Memory;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 钉死记忆切块的边界。
///
/// 切块决定检索质量：块超预算会被嵌入服务拒收，重叠没了则答案正好落在切口上就检索不到，
/// 上下文丢了则文档中段的块不知道自己属于哪一节——三种坏法都不会报错，
/// 只会让角色「记不起来」。
///
/// 与改动前的差别：预算从字符数换成 token 数（字符数在中英文之间差三四倍），
/// 切口优先落在结构边界上，且每块带来源与标题路径。
/// </summary>
public class MemoryTextChunkerTests
{
    /// <summary>空白文本不产生块，否则索引里会多出一批检索不到的空记录</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t \n")]
    public void BlankText_YieldsNothing(string text)
    {
        Assert.Empty(MemoryTextChunker.Split(text));
    }

    /// <summary>短文本切成一块，且换行归一成 \n、首尾空白去掉</summary>
    [Fact]
    public void ShortText_YieldsOneNormalizedChunk()
    {
        MemoryChunk[] chunks = MemoryTextChunker.Split("  第一行\r\n第二行  ").ToArray();

        Assert.Single(chunks);
        Assert.Equal("第一行\n第二行", chunks[0].Content);
    }

    /// <summary>
    /// 重叠不小于块预算会让切窗原地不动——每块都从上一块开头重来，永远推不到文本末尾。
    /// 这种参数必须当场拒绝，而不是靠 Math.Max 兜成前进 1 个字符然后生成海量碎块。
    /// </summary>
    [Theory]
    [InlineData(100, 100)]
    [InlineData(100, 200)]
    public void OverlapNotSmallerThanBudget_Throws(int maxTokens, int overlapTokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MemoryTextChunker.Split("abc", maxTokens: maxTokens, overlapTokens: overlapTokens).ToArray());
    }

    /// <summary>预算非正同样拒绝：算出来的块数会是无穷</summary>
    [Fact]
    public void NonPositiveBudget_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MemoryTextChunker.Split("abc", maxTokens: 0, overlapTokens: 0).ToArray());
    }

    /// <summary>每块都不超预算——这是对嵌入端点的唯一硬承诺</summary>
    [Fact]
    public void EveryChunk_StaysWithinBudget()
    {
        string text = string.Join("\n\n", Enumerable.Range(0, 80)
            .Select(i => $"第 {i} 段。这一段讲的是索引与检索的关系，写得足够长以便触发切分。"));

        MemoryChunk[] chunks = MemoryTextChunker.Split(text, "手册.md", maxTokens: 120, overlapTokens: 20).ToArray();

        Assert.NotEmpty(chunks);
        foreach (MemoryChunk chunk in chunks)
            Assert.True(LlmTokenizer.CountTokens(chunk.EmbeddingText) <= 120,
                $"块超出预算：{LlmTokenizer.CountTokens(chunk.EmbeddingText)} token");
    }

    /// <summary>
    /// 标题路径进上下文，并拼进真正送去嵌入的文本。
    /// 不拼的话，「macOS 怎么装」匹配不到标题在上一块里的那段正文。
    /// </summary>
    [Fact]
    public void HeaderPath_FlowsIntoContextAndEmbeddingText()
    {
        const string text = """
                            # 安装

                            ## macOS

                            用 brew 装就行。
                            """;

        MemoryChunk[] chunks = MemoryTextChunker.Split(text, "手册.md").ToArray();

        Assert.Single(chunks);
        Assert.Equal("手册.md / 安装 / macOS", chunks[0].Context);
        Assert.Equal("用 brew 装就行。", chunks[0].Content);
        Assert.StartsWith("手册.md / 安装 / macOS", chunks[0].EmbeddingText);
        Assert.EndsWith("用 brew 装就行。", chunks[0].EmbeddingText);
    }

    /// <summary>没有来源名时上下文只剩标题路径，不留下「 / 」这种空壳前缀</summary>
    [Fact]
    public void EmptySourceName_LeavesHeaderPathOnly()
    {
        MemoryChunk[] chunks = MemoryTextChunker.Split("# 标题\n\n正文").ToArray();

        Assert.Equal("标题", chunks[0].Context);
    }

    /// <summary>纯文本没有标题也没有来源名时，上下文为空，嵌入文本就是正文本身</summary>
    [Fact]
    public void PlainTextWithoutContext_EmbedsContentVerbatim()
    {
        MemoryChunk[] chunks = MemoryTextChunker.Split("就是一段普通文字").ToArray();

        Assert.Equal("", chunks[0].Context);
        Assert.Equal("就是一段普通文字", chunks[0].EmbeddingText);
    }

    /// <summary>
    /// 装得下的相邻小节合并成一块，各自的小节名写回正文。
    ///
    /// 早先的实现是「标题路径一变就断开」，理由是合并后上下文只能标其中一个小节、
    /// 另一节的内容就被贴上错误出处。代价是一份 400 个小节的文档切出 400 个碎块——
    /// 本地模型逐条嵌入，索引时间跟着翻好几倍。
    /// 现在改成合并 + 把小节名写进正文：出处没丢，块数回到正常量级。
    /// </summary>
    [Fact]
    public void SmallAdjacentSections_MergeAndKeepTheirTitlesInline()
    {
        const string text = """
                            # A

                            甲

                            # B

                            乙
                            """;

        MemoryChunk[] chunks = MemoryTextChunker.Split(text).ToArray();

        Assert.Single(chunks);

        // A 与 B 是同级兄弟、没有共同父标题,所以公共前缀为空,两个小节名都落进正文
        Assert.Equal("", chunks[0].Context);
        foreach (string fragment in (string[])["A", "甲", "B", "乙"])
            Assert.Contains(fragment, chunks[0].Content);
    }

    /// <summary>
    /// 共同父标题留在上下文里，只有分叉的那一段写回正文——
    /// 否则每块都把「安装」重复一遍，白占预算。
    /// </summary>
    [Fact]
    public void MergedSections_KeepSharedAncestorInContext()
    {
        const string text = """
                            # 安装

                            ## macOS

                            用 brew。

                            ## Windows

                            用 winget。
                            """;

        MemoryChunk[] chunks = MemoryTextChunker.Split(text, "手册.md").ToArray();

        Assert.Single(chunks);
        Assert.Equal("手册.md / 安装", chunks[0].Context);
        Assert.DoesNotContain("安装", chunks[0].Content);
        Assert.Contains("macOS", chunks[0].Content);
        Assert.Contains("Windows", chunks[0].Content);
    }

    /// <summary>装不下就还是各自成块，且各自带完整上下文</summary>
    [Fact]
    public void SectionsTooLargeToMerge_StaySeparateWithFullContext()
    {
        string bulk = string.Concat(Enumerable.Repeat("这一节的内容写得很长。", 40));
        string text = $"# 甲节\n\n{bulk}\n\n# 乙节\n\n{bulk}";

        MemoryChunk[] chunks = MemoryTextChunker.Split(text, "手册.md", maxTokens: 200, overlapTokens: 20).ToArray();

        Assert.True(chunks.Length >= 2);
        Assert.Contains(chunks, chunk => chunk.Context == "手册.md / 甲节");
        Assert.Contains(chunks, chunk => chunk.Context == "手册.md / 乙节");
    }

    /// <summary>
    /// 块预算跟着嵌入模型的上下文长度走。
    /// 写死常量会两头不讨好：8k 模型被切成碎块（索引慢一倍、检索还更差），
    /// 512 模型则每块都被端点拒收，全靠拆分兜底。
    /// </summary>
    [Theory]
    [InlineData(8192, MemoryTextChunker.MaxChunkTokensCeiling)] //大上下文封顶,块太大反而混主题
    [InlineData(512, 448)]
    [InlineData(256, 192)]
    [InlineData(64, MemoryTextChunker.MinChunkTokens)] //小得离谱时守住下限
    [InlineData(0, MemoryTextChunker.MaxChunkTokens)] //未配置时用兜底值
    [InlineData(-1, MemoryTextChunker.MaxChunkTokens)]
    public void ResolveChunkBudget_FollowsEmbeddingContext(int contextSize, int expected)
    {
        Assert.Equal(expected, MemoryTextChunker.ResolveChunkBudget(contextSize));
    }

    /// <summary>解析出来的预算必须始终大于默认重叠，否则 Split 会当场抛参数异常</summary>
    [Theory]
    [InlineData(8192)]
    [InlineData(512)]
    [InlineData(64)]
    [InlineData(0)]
    public void ResolveChunkBudget_AlwaysLeavesRoomForOverlap(int contextSize)
    {
        Assert.True(MemoryTextChunker.ResolveChunkBudget(contextSize) > MemoryTextChunker.ChunkOverlapTokens);
    }

    /// <summary>同一标题下的多个段落装得下就合并成一块，不必一段一块</summary>
    [Fact]
    public void ParagraphsUnderSameHeader_MergeWhenTheyFit()
    {
        const string text = """
                            # 标题

                            第一段。

                            第二段。
                            """;

        MemoryChunk[] chunks = MemoryTextChunker.Split(text).ToArray();

        Assert.Single(chunks);
        Assert.Contains("第一段。", chunks[0].Content);
        Assert.Contains("第二段。", chunks[0].Content);
    }

    /// <summary>相邻块要有重叠：答案落在切口上时，至少在一块里是完整的</summary>
    [Fact]
    public void AdjacentChunks_ShareOverlappingContent()
    {
        string text = string.Join("\n\n", Enumerable.Range(0, 40)
            .Select(i => $"paragraph {i} with enough words to make the chunker split this text apart"));

        MemoryChunk[] chunks = MemoryTextChunker.Split(text, maxTokens: 100, overlapTokens: 30).ToArray();

        Assert.True(chunks.Length >= 2, "参数应当足以切出多块");
        for (int i = 1; i < chunks.Length; i++)
        {
            string previousTail = chunks[i - 1].Content;
            string currentHead = chunks[i].Content;
            Assert.True(HasSharedFragment(previousTail, currentHead),
                $"第 {i} 块与上一块没有任何重叠");
        }
    }

    /// <summary>
    /// 中文长句没有空白可依，切口要落在句末标点上。
    /// 只按空白找的话中文正文会被从正中间硬切——对中文优先的内容这是实打实的质量损失。
    /// </summary>
    [Fact]
    public void ChineseText_CutsAtSentenceEnders()
    {
        string text = string.Concat(Enumerable.Range(0, 40)
            .Select(i => $"这是第{i}个句子，讲的是记忆索引怎么切块。"));

        MemoryChunk[] chunks = MemoryTextChunker.Split(text, maxTokens: 120, overlapTokens: 20).ToArray();

        Assert.True(chunks.Length >= 2, "参数应当足以切出多块");

        // 末块之外都应当停在句末标点上,而不是停在句子中间
        for (int i = 0; i < chunks.Length - 1; i++)
            Assert.EndsWith("。", chunks[i].Content);
    }

    /// <summary>围栏代码块不被当成 markdown 结构解读：里面的 # 是注释，不是标题</summary>
    [Fact]
    public void CodeFence_DoesNotLeakIntoHeaderPath()
    {
        const string text = """
                            # 用法

                            ```bash
                            # 这是注释而不是标题
                            brew install foo
                            ```
                            """;

        MemoryChunk[] chunks = MemoryTextChunker.Split(text).ToArray();

        Assert.All(chunks, chunk => Assert.Equal("用法", chunk.Context));
        Assert.Contains("brew install foo", chunks[0].Content);
    }

    /// <summary>表格被切开时每片都要带上表头行，否则后面几片的列没有含义</summary>
    [Fact]
    public void OversizedTable_RepeatsHeaderOnEveryPiece()
    {
        string rows = string.Join("\n", Enumerable.Range(0, 60)
            .Select(i => $"| 行{i} | 这一列写了不少字用来把表格撑过预算 | {i * 1000} |"));
        string text = "| 名称 | 说明 | 数值 |\n| --- | --- | --- |\n" + rows;

        MemoryChunk[] chunks = MemoryTextChunker.Split(text, maxTokens: 150, overlapTokens: 20).ToArray();

        Assert.True(chunks.Length >= 2, "参数应当足以把表格切开");
        Assert.All(chunks, chunk => Assert.Contains("| 名称 | 说明 | 数值 |", chunk.Content));
    }

    /// <summary>超长块在就近的句末标点或空白处断开，两段拼回原文（接缝空白被 Trim 掉）</summary>
    [Fact]
    public void SplitOversized_BreaksAtNearbyWhitespace()
    {
        (string first, string second) = MemoryTextChunker.SplitOversized("aaaa bbbb cccc dddd");

        Assert.Equal("aaaa bbbb", first);
        Assert.Equal("cccc dddd", second);
    }

    /// <summary>中文超长块优先在句号处断开，而不是从正中间硬切</summary>
    [Fact]
    public void SplitOversized_PrefersChineseSentenceEnder()
    {
        (string first, string second) = MemoryTextChunker.SplitOversized("前半段内容。后半段内容");

        Assert.Equal("前半段内容。", first);
        Assert.Equal("后半段内容", second);
    }

    /// <summary>连标点都没有的中文正文，从正中间硬切，且不丢字</summary>
    [Fact]
    public void SplitOversized_ChineseTextHalvesWithoutLosingCharacters()
    {
        string text = string.Concat(Enumerable.Repeat("记忆索引", 10));

        (string first, string second) = MemoryTextChunker.SplitOversized(text);

        Assert.Equal(text.Length / 2, first.Length);
        Assert.Equal(text, first + second);
    }

    /// <summary>找到的边界若让某一段全空，退回正中间硬切，绝不返回空段</summary>
    [Fact]
    public void SplitOversized_NeverReturnsEmptyPart()
    {
        (string first, string second) = MemoryTextChunker.SplitOversized("     x");

        Assert.False(string.IsNullOrEmpty(first));
        Assert.False(string.IsNullOrEmpty(second));
    }

    /// <summary>搜索半径之外的边界不算：远处有空白也照样从正中间切</summary>
    [Fact]
    public void SplitOversized_IgnoresWhitespaceBeyondSearchRadius()
    {
        string text = new string('a', 200) + " " + new string('b', 600); //唯一的空白离正中间 200 字符
        int middle = text.Length / 2;

        (string first, string second) = MemoryTextChunker.SplitOversized(text);

        Assert.Equal(text[..middle], first);
        Assert.Equal(text[middle..], second);
    }

    /// <summary>短到不值得再拆的块不再对半分，避免拆成一堆检索不到的碎片</summary>
    [Fact]
    public void CanSplitFurther_StopsAtMinimumLength()
    {
        Assert.False(MemoryTextChunker.CanSplitFurther(new string('a', MemoryTextChunker.MinimumSplitLength)));
        Assert.True(MemoryTextChunker.CanSplitFurther(new string('a', MemoryTextChunker.MinimumSplitLength + 1)));
    }

    /// <summary>同输入同输出：索引可以重建，切块结果不能跟着调用次数变</summary>
    [Fact]
    public void Split_IsDeterministic()
    {
        string text = string.Join("\r\n", Enumerable.Range(0, 300).Select(i => $"第 {i} 行 line {i}"));

        Assert.Equal(
            MemoryTextChunker.Split(text, "a.md").ToArray(),
            MemoryTextChunker.Split(text, "a.md").ToArray());
        Assert.Equal(MemoryTextChunker.SplitOversized(text), MemoryTextChunker.SplitOversized(text));
    }

    /// <summary>
    /// 切块不丢内容：每个非空段落都必须至少出现在某一块里。
    /// 这条比「拼起来等于原文」宽松，因为结构切块会丢弃标题行（它们进了上下文）并归一空行。
    /// </summary>
    [Fact]
    public void Split_KeepsEveryParagraph()
    {
        string[] paragraphs = Enumerable.Range(0, 60).Select(i => $"段落 {i} paragraph body").ToArray();
        string text = string.Join("\n\n", paragraphs);

        MemoryChunk[] chunks = MemoryTextChunker.Split(text, maxTokens: 120, overlapTokens: 20).ToArray();

        foreach (string paragraph in paragraphs)
            Assert.Contains(chunks, chunk => chunk.Content.Contains(paragraph, StringComparison.Ordinal));
    }

    /// <summary>标题行本身不进正文——它已经在上下文里，重复一遍等于挤占预算</summary>
    [Fact]
    public void HeadingLines_DoNotAppearInContent()
    {
        MemoryChunk[] chunks = MemoryTextChunker.Split("# 标题行\n\n正文内容").ToArray();

        Assert.DoesNotContain("# 标题行", chunks[0].Content);
        Assert.Equal("正文内容", chunks[0].Content);
    }

    /// <summary>两块之间是否共享了一段连续文字（重叠的判据，避开对具体切点的依赖）</summary>
    private static bool HasSharedFragment(string previous, string current)
    {
        const int fragmentLength = 12;
        for (int start = 0; start + fragmentLength <= current.Length; start++)
        {
            if (previous.Contains(current.Substring(start, fragmentLength), StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
