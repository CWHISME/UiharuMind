using UiharuMind.Core.AI.Memory;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 钉死结构识别。
///
/// 这一层只认 markdown 语法、不认 token 预算，所以能单独钉住「什么算一个块、块归哪个标题」。
/// 认错的后果不会报错：标题被当成正文就白占预算，围栏里的 # 被当成标题就把代码切成两半，
/// 表格与正文粘成一块则表头行跟数据行分了家。
///
/// 纯文本是 markdown 的合法子集，所以 .txt 走进来就是按空行分段——这也是本类不需要
/// 「是不是 markdown」判断的原因。
/// </summary>
public class MarkdownBlockScannerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t \n")]
    public void BlankText_YieldsNothing(string text)
    {
        Assert.Empty(MarkdownBlockScanner.Scan(text));
    }

    /// <summary>空行分段，这是纯文本走进来时唯一起作用的规则</summary>
    [Fact]
    public void BlankLine_SeparatesParagraphs()
    {
        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan("第一段\n\n第二段").ToArray();

        Assert.Equal(2, blocks.Length);
        Assert.All(blocks, block => Assert.Equal(EMemoryBlockKind.Paragraph, block.Kind));
        Assert.Equal("第一段", blocks[0].Text);
        Assert.Equal("第二段", blocks[1].Text);
    }

    /// <summary>段内换行不分段：一段里的软换行仍属同一语义单元</summary>
    [Fact]
    public void SingleNewline_KeepsOneParagraph()
    {
        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan("第一行\n第二行").ToArray();

        Assert.Single(blocks);
        Assert.Equal("第一行\n第二行", blocks[0].Text);
    }

    /// <summary>标题不进正文，只进标题路径</summary>
    [Fact]
    public void Heading_BecomesPathNotContent()
    {
        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan("# 标题\n\n正文").ToArray();

        Assert.Single(blocks);
        Assert.Equal("正文", blocks[0].Text);
        Assert.Equal("标题", blocks[0].HeaderPath);
    }

    /// <summary>多级标题拼成路径</summary>
    [Fact]
    public void NestedHeadings_BuildPath()
    {
        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan("# 一\n\n## 二\n\n### 三\n\n正文").ToArray();

        Assert.Equal("一 / 二 / 三", blocks[0].HeaderPath);
    }

    /// <summary>
    /// 回到浅层标题时，更深的标题必须失效。
    /// 不清的话「## 卸载」下的正文会带着上一节的「### macOS」，出处就是错的。
    /// </summary>
    [Fact]
    public void ShallowerHeading_ClearsDeeperOnes()
    {
        const string text = """
                            # 安装

                            ## macOS

                            甲

                            # 卸载

                            乙
                            """;

        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan(text).ToArray();

        Assert.Equal(2, blocks.Length);
        Assert.Equal("安装 / macOS", blocks[0].HeaderPath);
        Assert.Equal("卸载", blocks[1].HeaderPath);
    }

    /// <summary>标题前没有空行也要断开——否则上一段会被并进新标题名下</summary>
    [Fact]
    public void HeadingWithoutBlankLineBefore_StillBreaks()
    {
        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan("正文甲\n# 标题\n正文乙").ToArray();

        Assert.Equal(2, blocks.Length);
        Assert.Equal("正文甲", blocks[0].Text);
        Assert.Equal("", blocks[0].HeaderPath);
        Assert.Equal("正文乙", blocks[1].Text);
        Assert.Equal("标题", blocks[1].HeaderPath);
    }

    /// <summary>7 个 # 不是标题（markdown 只到 6 级），井号后没有空白也不是</summary>
    [Theory]
    [InlineData("####### 七级")]
    [InlineData("#没有空格")]
    [InlineData("#")]
    public void NotAHeading_StaysParagraph(string line)
    {
        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan(line).ToArray();

        Assert.Single(blocks);
        Assert.Equal(EMemoryBlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal("", blocks[0].HeaderPath);
    }

    /// <summary>围栏内原样收下：里面的 # 是注释，不是标题</summary>
    [Fact]
    public void CodeFence_KeepsInteriorVerbatim()
    {
        const string text = """
                            ```bash
                            # 注释
                            brew install foo
                            ```
                            """;

        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan(text).ToArray();

        Assert.Single(blocks);
        Assert.Equal(EMemoryBlockKind.Code, blocks[0].Kind);
        Assert.Contains("# 注释", blocks[0].Text);
        Assert.Contains("brew install foo", blocks[0].Text);
        Assert.Equal("", blocks[0].HeaderPath);
    }

    /// <summary>围栏里的空行不分段——代码中间空一行是排版，不是段落边界</summary>
    [Fact]
    public void CodeFence_BlankLineInsideDoesNotSplit()
    {
        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan("```\na\n\nb\n```").ToArray();

        Assert.Single(blocks);
        Assert.Equal(EMemoryBlockKind.Code, blocks[0].Kind);
    }

    /// <summary>~~~ 也是围栏，只认 ``` 会把这类文档的代码当正文切开</summary>
    [Fact]
    public void TildeFence_IsRecognized()
    {
        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan("~~~\n# 注释\n~~~").ToArray();

        Assert.Single(blocks);
        Assert.Equal(EMemoryBlockKind.Code, blocks[0].Kind);
    }

    /// <summary>没有闭合围栏的文档不该丢内容：收到文末为止</summary>
    [Fact]
    public void UnclosedFence_StillYieldsContent()
    {
        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan("```\nabc").ToArray();

        Assert.Single(blocks);
        Assert.Contains("abc", blocks[0].Text);
    }

    /// <summary>连续的表格行成一块，标成表格类型</summary>
    [Fact]
    public void TableRows_FormOneTableBlock()
    {
        const string text = "| a | b |\n| --- | --- |\n| 1 | 2 |";

        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan(text).ToArray();

        Assert.Single(blocks);
        Assert.Equal(EMemoryBlockKind.Table, blocks[0].Kind);
    }

    /// <summary>表格与正文之间没有空行也要断开，否则表头跟正文粘成一块</summary>
    [Fact]
    public void TableAdjacentToParagraph_BreaksWithoutBlankLine()
    {
        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan("正文\n| a | b |\n| --- | --- |\n后续正文").ToArray();

        Assert.Equal(3, blocks.Length);
        Assert.Equal(EMemoryBlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal(EMemoryBlockKind.Table, blocks[1].Kind);
        Assert.Equal(EMemoryBlockKind.Paragraph, blocks[2].Kind);
        Assert.Equal("后续正文", blocks[2].Text);
    }

    /// <summary>表头含分隔行时一起取出，供按行切开时重复带上</summary>
    [Fact]
    public void GetTableHeader_IncludesSeparatorRow()
    {
        string header = MarkdownBlockScanner.GetTableHeader("| a | b |\n| --- | --- |\n| 1 | 2 |");

        Assert.Equal("| a | b |\n| --- | --- |", header);
    }

    /// <summary>没有分隔行时只取首行</summary>
    [Fact]
    public void GetTableHeader_WithoutSeparatorTakesFirstRowOnly()
    {
        string header = MarkdownBlockScanner.GetTableHeader("| a | b |\n| 1 | 2 |");

        Assert.Equal("| a | b |", header);
    }

    /// <summary>不是表格就没有表头，别硬拿首行当表头贴到每一片上</summary>
    [Fact]
    public void GetTableHeader_ReturnsEmptyForNonTable()
    {
        Assert.Equal("", MarkdownBlockScanner.GetTableHeader("普通正文\n第二行"));
    }

    /// <summary>\r\n 与裸 \r 都要归一，否则 Windows 换行的文档分不出段</summary>
    [Theory]
    [InlineData("甲\r\n\r\n乙")]
    [InlineData("甲\r\r乙")]
    public void CarriageReturns_AreNormalized(string text)
    {
        MemoryTextBlock[] blocks = MarkdownBlockScanner.Scan(text).ToArray();

        Assert.Equal(2, blocks.Length);
        Assert.Equal("甲", blocks[0].Text);
        Assert.Equal("乙", blocks[1].Text);
    }

    /// <summary>同输入同输出</summary>
    [Fact]
    public void Scan_IsDeterministic()
    {
        const string text = "# 一\n\n甲\n\n```\nx\n```\n\n| a |\n| --- |";

        Assert.Equal(
            MarkdownBlockScanner.Scan(text).ToArray(),
            MarkdownBlockScanner.Scan(text).ToArray());
    }
}
