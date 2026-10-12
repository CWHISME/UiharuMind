using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉住 Read 的「行号→」前缀：界面剥得干净、读得出真实行号；Edit 只在确认是从 Read 抄来时才剥，
/// 不误伤真以「数字→」开头的原文
/// </summary>
public sealed class ReadLineNumbersTests
{
    [Fact]
    public void Format_RightAlignsNumbers()
    {
        Assert.Equal(" 9→a\n10→b", ReadLineNumbers.Format(["a", "b"], 9));
    }

    [Fact]
    public void Strip_ReturnsContentFirstLineAndCount_LeavingNotesAlone()
    {
        ReadContent content = ReadLineNumbers.Strip(" 9→a\n10→b\n…[已截断：显示第 9–10 行，共 40 行；续读传 offset=11]");

        Assert.Equal("a\nb\n…[已截断：显示第 9–10 行，共 40 行；续读传 offset=11]", content.Text);
        Assert.Equal(9, content.FirstLine);
        Assert.Equal(2, content.ContentLines);
    }

    [Fact]
    public void Edit_StripsPrefixesCopiedFromRead_AndSaysSo()
    {
        FileEditPlan plan = FileEditPlanner.Plan("x\nfoo()\nbar()\n", "a.cs",
            [new FileEdit { OldString = "2→foo()\n3→bar()", NewString = "2→foo()\nbaz()\n3→bar()" }]);

        Assert.True(plan.Succeeded, plan.Error);
        Assert.True(plan.StrippedLinePrefixes);
        Assert.Equal("x\nfoo()\nbaz()\nbar()\n", plan.NewText);
    }

    /// <summary>文件里真有「数字→」开头的行：原样就能命中，不剥，newString 原样写入</summary>
    [Fact]
    public void Edit_KeepsLiteralArrowContent_WhenItMatchesAsIs()
    {
        FileEditPlan plan = FileEditPlanner.Plan("1→开始\n2→结束\n", "steps.md",
            [new FileEdit { OldString = "1→开始\n2→结束", NewString = "1→开始\n2→中间\n3→结束" }]);

        Assert.True(plan.Succeeded, plan.Error);
        Assert.False(plan.StrippedLinePrefixes);
        Assert.Equal("1→开始\n2→中间\n3→结束\n", plan.NewText);
    }

    /// <summary>行号不连续不像是 Read 抄来的，不剥</summary>
    [Fact]
    public void TryStripCopied_RequiresConsecutiveNumbers()
    {
        Assert.True(ReadLineNumbers.TryStripCopied(" 9→a\n10→b\n", out string stripped));
        Assert.Equal("a\nb\n", stripped);
        Assert.False(ReadLineNumbers.TryStripCopied("1→a\n3→b", out _));
        Assert.False(ReadLineNumbers.TryStripCopied("1→a\nb", out _));
    }
}
