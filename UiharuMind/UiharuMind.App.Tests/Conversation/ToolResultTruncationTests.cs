using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 工具结果纯文本的截断规则。
///
/// 它解决的是一个渲染成本问题：结果面板外层的 <c>ScrollViewer MaxHeight</c> 只裁视口不裁
/// 文本布局，整篇几十万字每次布局都要重新断行，而会话流没有虚拟化——于是流式回复期间
/// 每一帧都在为早已跑完的卡片付这笔钱。截断把它压回常数级。
///
/// 两个阈值必须<b>都</b>拦得住：只看行数拦不住 MCP 那种一整行几百 KB 的 JSON，
/// 只看字符数拦不住几万条短行。
/// </summary>
public class ToolResultTruncationTests
{
    private static string Lines(int count, string text = "line")
        => string.Join('\n', Enumerable.Range(0, count).Select(i => $"{text}{i}"));

    [Fact]
    public void ShortResult_PassesThroughUntouched()
    {
        const string text = "ok\ndone";

        ToolResultView view = ToolResultTruncation.Build(text);

        Assert.Equal(text, view.DisplayText);
        Assert.False(view.IsTruncated);
        Assert.Equal(2, view.TotalLines);
        Assert.Equal(2, view.KeptLines);
        Assert.Equal(0, view.OmittedChars);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyResult_HasNoContentAndNoTruncation(string? text)
    {
        ToolResultView view = ToolResultTruncation.Build(text);

        Assert.Equal(string.Empty, view.DisplayText);
        Assert.False(view.IsTruncated);
        Assert.Equal(0, view.TotalLines);
    }

    /// <summary>恰好压在行数上限上不该被截：阈值是"超过才截"，否则提示行会为 0 行省略而出现</summary>
    [Fact]
    public void ExactlyAtTheLineLimit_IsNotTruncated()
    {
        ToolResultView view = ToolResultTruncation.Build(Lines(ToolResultTruncation.MaxLines));

        Assert.False(view.IsTruncated);
        Assert.Equal(ToolResultTruncation.MaxLines, view.TotalLines);
    }

    [Fact]
    public void ManyShortLines_AreCutAtTheLineLimit()
    {
        const int total = 5000;

        ToolResultView view = ToolResultTruncation.Build(Lines(total));

        Assert.True(view.IsTruncated);
        Assert.Equal(total, view.TotalLines);
        Assert.Equal(ToolResultTruncation.MaxLines, view.KeptLines);
        Assert.True(view.OmittedChars > 0);
        Assert.StartsWith("line0\n", view.DisplayText);
        Assert.DoesNotContain($"line{ToolResultTruncation.MaxLines}", view.DisplayText); //压线的那行在上限之外
    }

    /// <summary>行数没超但体积超了：十几行、每行 1KB 的结果同样要拦</summary>
    [Fact]
    public void FewButHugeLines_AreCutAtTheCharLimit()
    {
        int lineCount = ToolResultTruncation.MaxLines / 2; //行数离上限还远
        string text = Lines(lineCount, new string('x', ToolResultTruncation.MaxChars / 2));

        ToolResultView view = ToolResultTruncation.Build(text);

        Assert.True(view.IsTruncated);
        Assert.True(view.KeptLines < lineCount, $"应在行数上限之前先被体积拦住，实际保了 {view.KeptLines} 行");
        Assert.True(view.DisplayText.Length <= ToolResultTruncation.MaxChars);
    }

    /// <summary>
    /// 一整行几百 KB 的 JSON（MCP 结果的常态）：没有换行可断，必须在行中间切，
    /// 否则字符上限形同虚设，而这正是最贵的一种结果。
    /// </summary>
    [Fact]
    public void SingleGiantLine_IsCutMidLine()
    {
        string text = new('j', 500 * 1024);

        ToolResultView view = ToolResultTruncation.Build(text);

        Assert.True(view.IsTruncated);
        Assert.Equal(ToolResultTruncation.MaxChars, view.DisplayText.Length);
        Assert.Equal(1, view.TotalLines);
        Assert.Equal(text.Length - ToolResultTruncation.MaxChars, view.OmittedChars);
    }

    /// <summary>截断只发生在头部：省掉的永远是尾巴，开头一定原样保留</summary>
    [Fact]
    public void TruncationKeepsTheHead()
    {
        string text = "首行很重要\n" + Lines(5000);

        ToolResultView view = ToolResultTruncation.Build(text);

        Assert.StartsWith("首行很重要\n", view.DisplayText);
        Assert.StartsWith(view.DisplayText, text); //展示正文就是原文的前缀,一字未改
    }

    /// <summary>卡片绑的是 <c>ResultDisplayText</c>，原文仍在 <c>ResultText</c> 里，一字不少</summary>
    [Fact]
    public void Card_RendersTheTruncatedTextButKeepsTheOriginal()
    {
        string text = Lines(5000);
        ToolCallItem item = new() { ToolName = "run_shell" };

        item.ResultText = text;

        Assert.True(item.IsResultTruncated);
        Assert.Equal(text, item.ResultText);
        Assert.True(item.ResultDisplayText.Length < text.Length);
    }

    /// <summary>换一份结果要整份重算：上一份的截断视图不该漏给下一份</summary>
    [Fact]
    public void ANewResult_RebuildsTheView()
    {
        ToolCallItem item = new() { ToolName = "run_shell" };
        item.ResultText = Lines(5000);

        item.ResultText = "ok";

        Assert.False(item.IsResultTruncated);
        Assert.Equal("ok", item.ResultDisplayText);
    }

    /// <summary>
    /// 参数原文走同一套阈值。它此前<b>零截断</b>——转录器把参数原样 join，
    /// 一次 <c>Write</c> 带几百 KB content，卡片一展开就直接进排版。
    /// </summary>
    [Fact]
    public void HugeArguments_AreTruncatedToo()
    {
        string huge = new('c', 500 * 1024);
        ToolCallItem item = new() { ToolName = "Write" };

        item.ArgumentsJson = $"content: {huge}";

        Assert.True(item.IsArgumentsTruncated);
        Assert.Equal(ToolResultTruncation.MaxChars, item.ArgumentsDisplayText.Length);
        Assert.Equal($"content: {huge}", item.ArgumentsJson); //原文一字不少,↗ 拿的是它
    }

    /// <summary>
    /// 短行开头 + 巨大单行：字符预算要<b>花满</b>，不能退回上一行末尾。
    ///
    /// 曾经 <c>FindCut</c> 在"不是第一行"时回退到上一行末，想的是别把一行切两半，
    /// 代价是这种结果的预览只剩开头那八个字符——行数与字符数都是<b>上限</b>而非<b>目标</b>。
    /// </summary>
    [Fact]
    public void ShortHeaderThenGiantLine_StillSpendsTheWholeBudget()
    {
        const string header = "Result:\n";
        string text = header + new string('j', 500 * 1024);

        ToolResultView view = ToolResultTruncation.Build(text);

        Assert.True(view.IsTruncated);
        Assert.Equal(ToolResultTruncation.MaxChars, view.DisplayText.Length);
        Assert.StartsWith(header, view.DisplayText);
    }

    /// <summary>
    /// 行中间切时不能劈开代理对，否则预览末尾留一个孤立代理项、渲染成乱码方块。
    /// 中日文与 emoji 的结果很容易撞到这里。
    /// </summary>
    [Fact]
    public void MidLineCut_NeverSplitsASurrogatePair()
    {
        //让 😀 的高位代理项正好落在期望切点的前一格
        string text = new string('a', ToolResultTruncation.MaxChars - 1) + "😀" + new string('b', 1024);

        ToolResultView view = ToolResultTruncation.Build(text);

        Assert.True(view.IsTruncated);
        Assert.Equal(ToolResultTruncation.MaxChars - 1, view.DisplayText.Length);
        Assert.False(char.IsSurrogate(view.DisplayText[^1]));
    }

    /// <summary>
    /// diff 路径是逐行独立 TextBlock，不吃整篇重排那份成本，而且它在<b>源头</b>就被封死了——
    /// Core 侧 <c>PermissiveFileAccessTools.MaxEditDiffLines = 80</c> 决定了回给模型的 diff 文本
    /// 最多 80 行，卡片是从那段正文认回来的，所以实际到不了 <see cref="DiffLineView"/> 的 300 行上限
    /// （那个上限只在审批卡那条路上活着）。截断不该插手它，否则提示行会挂在一份根本没被截的 diff 下面。
    /// </summary>
    [Fact]
    public void DiffResults_AreLeftToTheDiffPath()
    {
        ToolCallItem item = new() { ToolName = "Edit" };
        string diff = "Applied 1 edit(s) to 'x.cs'.\n"
                      + string.Join('\n', Enumerable.Range(0, 4000).Select(i => $"+{i,5} added line {i}"));

        item.ResultText = diff;

        Assert.True(item.HasResultDiff);
        Assert.False(item.IsResultTruncated);
    }
}
