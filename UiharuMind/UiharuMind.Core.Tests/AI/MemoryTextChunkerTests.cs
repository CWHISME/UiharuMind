using UiharuMind.Core.AI.Memory;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 钉死记忆切块的边界。
///
/// 切块决定检索质量：块太长会被嵌入服务拒收，重叠没了则答案正好落在切口上就检索不到，
/// 而这两种坏法都不会报错——只会让角色"记不起来"。本文件按现状取快照，
/// 切块器从 <c>MemoryData</c> 搬出来时靠它保证行为逐字未变。
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
        string[] chunks = MemoryTextChunker.Split("  第一行\r\n第二行  ").ToArray();

        Assert.Equal(["第一行\n第二行"], chunks);
    }

    /// <summary>
    /// 用小参数把切窗走位钉死：起点每次前移 maxLength - overlap，末块只到文本结尾。
    /// </summary>
    [Fact]
    public void LongText_StepsByMaxLengthMinusOverlap()
    {
        string[] chunks = MemoryTextChunker.Split("abcdefghij", maxLength: 5, overlap: 2).ToArray();

        Assert.Equal(["abcde", "defgh", "ghij"], chunks);
    }

    /// <summary>默认参数（900 / 120）下的块长与起点：900 / 900 / 440，第二块起于 780</summary>
    [Fact]
    public void DefaultParameters_KeepChunkLengthAndOverlap()
    {
        string text = string.Concat(Enumerable.Range(0, 2000).Select(i => (char)('A' + i % 26)));

        string[] chunks = MemoryTextChunker.Split(text).ToArray();

        Assert.Equal([900, 900, 440], chunks.Select(x => x.Length).ToArray());
        Assert.Equal(text[780..1680], chunks[1]);
        Assert.Equal(text[1560..2000], chunks[2]);
    }

    /// <summary>重叠必须真的落在内容上：相邻块的接缝处内容重合，答案不会被切口吃掉</summary>
    [Fact]
    public void AdjacentChunks_ShareOverlappingContent()
    {
        string text = string.Concat(Enumerable.Range(0, 200).Select(i => (char)('A' + i % 26)));

        string[] chunks = MemoryTextChunker.Split(text, maxLength: 100, overlap: 30).ToArray();

        Assert.Equal(text[70..170], chunks[1]);
        Assert.Equal(chunks[0][^30..], chunks[1][..30]);
    }

    /// <summary>overlap 不小于 maxLength 时仍必须前进，否则会原地死循环</summary>
    [Fact]
    public void OverlapNotSmallerThanMaxLength_StillAdvances()
    {
        string[] chunks = MemoryTextChunker.Split("abcdef", maxLength: 3, overlap: 5).ToArray();

        Assert.Equal(["abc", "bcd", "cde", "def"], chunks);
    }

    /// <summary>超长块在就近的空白处断开，两段拼回原文（接缝空白被 Trim 掉）</summary>
    [Fact]
    public void SplitOversized_BreaksAtNearbyWhitespace()
    {
        (string first, string second) = MemoryTextChunker.SplitOversized("aaaa bbbb cccc dddd");

        Assert.Equal("aaaa bbbb", first);
        Assert.Equal("cccc dddd", second);
    }

    /// <summary>中文正文没有空白可用，从正中间硬切，且不丢字</summary>
    [Fact]
    public void SplitOversized_ChineseTextHalvesWithoutLosingCharacters()
    {
        string text = string.Concat(Enumerable.Repeat("记忆索引", 10));

        (string first, string second) = MemoryTextChunker.SplitOversized(text);

        Assert.Equal(text.Length / 2, first.Length);
        Assert.Equal(text, first + second);
    }

    /// <summary>找到的空白若让某一段全空，退回正中间硬切，绝不返回空段</summary>
    [Fact]
    public void SplitOversized_NeverReturnsEmptyPart()
    {
        (string first, string second) = MemoryTextChunker.SplitOversized("     x");

        Assert.False(string.IsNullOrEmpty(first));
        Assert.False(string.IsNullOrEmpty(second));
    }

    /// <summary>搜索半径之外的空白不算：远处有空白也照样从正中间切</summary>
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

    /// <summary>同输入同输出：索引可以增量重建，切块结果不能跟着调用次数变</summary>
    [Fact]
    public void Split_IsDeterministic()
    {
        string text = string.Join("\r\n", Enumerable.Range(0, 300).Select(i => $"第 {i} 行 line {i}"));

        Assert.Equal(MemoryTextChunker.Split(text).ToArray(), MemoryTextChunker.Split(text).ToArray());
        Assert.Equal(MemoryTextChunker.SplitOversized(text), MemoryTextChunker.SplitOversized(text));
    }

    /// <summary>切块不丢内容：把重叠部分去掉后逐块拼接，应当等于归一化后的原文</summary>
    [Fact]
    public void Split_CoversWholeTextWithoutGap()
    {
        string text = string.Join("\n", Enumerable.Range(0, 120).Select(i => $"段落 {i} paragraph body"));
        string normalized = text.Replace("\r\n", "\n").Trim();

        string[] chunks = MemoryTextChunker.Split(normalized).ToArray();

        int cursor = 0;
        foreach (string chunk in chunks)
        {
            int index = normalized.IndexOf(chunk, Math.Max(0, cursor - chunk.Length), StringComparison.Ordinal);
            Assert.True(index >= 0 && index <= cursor, "块必须原样出现在原文里且不跳过内容");
            cursor = index + chunk.Length;
        }

        Assert.Equal(normalized.Length, cursor);
    }
}
