using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉住 Read 的「行号+制表符」前缀：界面剥得干净、读得出真实行号；Edit 只在确认是从 Read 抄来时才剥，
/// 不误伤真以「数字+制表符」开头的原文
/// </summary>
public sealed class ReadLineNumbersTests
{
    [Fact]
    public void Format_LeftAlignsNumbers_NoPadding()
    {
        Assert.Equal("9\ta\n10\tb", ReadLineNumbers.Format(["a", "b"], 9));
        Assert.Equal("100\tx\n101\ty", ReadLineNumbers.Format(["x", "y"], 100));
    }

    [Fact]
    public void Strip_ReturnsContentFirstLineAndCount_LeavingNotesAlone()
    {
        ReadContent content = ReadLineNumbers.Strip("9\ta\n10\tb\n…[已截断：显示第 9–10 行，共 40 行；续读传 offset=11]");

        Assert.Equal("a\nb\n…[已截断：显示第 9–10 行，共 40 行；续读传 offset=11]", content.Text);
        Assert.Equal(9, content.FirstLine);
        Assert.Equal(2, content.ContentLines);
    }

    [Fact]
    public void Edit_StripsPrefixesCopiedFromRead_AndSaysSo()
    {
        FileEditPlan plan = FileEditPlanner.Plan("x\nfoo()\nbar()\n", "a.cs",
            [new FileEdit { OldString = "2\tfoo()\n3\tbar()", NewString = "2\tfoo()\nbaz()\n3\tbar()" }]);

        Assert.True(plan.Succeeded, plan.Error);
        Assert.True(plan.StrippedLinePrefixes);
        Assert.Equal("x\nfoo()\nbaz()\nbar()\n", plan.NewText);
    }

    /// <summary>文件里真有「数字+制表符」开头的行：原样就能命中，不剥，newString 原样写入</summary>
    [Fact]
    public void Edit_KeepsLiteralArrowContent_WhenItMatchesAsIs()
    {
        FileEditPlan plan = FileEditPlanner.Plan("1\t开始\n2\t结束\n", "steps.md",
            [new FileEdit { OldString = "1\t开始\n2\t结束", NewString = "1\t开始\n2\t中间\n3\t结束" }]);

        Assert.True(plan.Succeeded, plan.Error);
        Assert.False(plan.StrippedLinePrefixes);
        Assert.Equal("1\t开始\n2\t中间\n3\t结束\n", plan.NewText);
    }

    /// <summary>行号不连续不像是 Read 抄来的，不剥</summary>
    [Fact]
    public void TryStripCopied_RequiresConsecutiveNumbers()
    {
        Assert.True(ReadLineNumbers.TryStripCopied("9\ta\n10\tb\n", out string stripped));
        Assert.Equal("a\nb\n", stripped);
        Assert.False(ReadLineNumbers.TryStripCopied("1\ta\n3\tb", out _));
        Assert.False(ReadLineNumbers.TryStripCopied("1\ta\nb", out _));
    }

    /// <summary>删了行号、留着其后的制表符：每行多一个行首 \t，剥一个再匹配，newString 每行都带也一起剥</summary>
    [Fact]
    public void Edit_StripsLeftoverSeparatorTab_WhenNumbersWereRemoved()
    {
        FileEditPlan plan = FileEditPlanner.Plan("{\n\tint a;\n\n\tint b;\n}\n", "a.cpp",
            [new FileEdit { OldString = "\t\tint a;\n\t\n\t\tint b;", NewString = "\t\tint a = 1;\n\t\n\t\tint b = 2;" }]);

        Assert.True(plan.Succeeded, plan.Error);
        Assert.True(plan.StrippedLinePrefixes);
        Assert.Equal("{\n\tint a = 1;\n\n\tint b = 2;\n}\n", plan.NewText);
    }

    /// <summary>newString 混着带与不带的行：多半是新写的行，原样写入不剥</summary>
    [Fact]
    public void Edit_KeepsNewString_WhenOnlySomeLinesCarryTheTab()
    {
        FileEditPlan plan = FileEditPlanner.Plan("{\n\tint a;\n}\n", "a.cpp",
            [new FileEdit { OldString = "\t\tint a;", NewString = "\t\tint a;\n#define C" }]);

        Assert.True(plan.Succeeded, plan.Error);
        Assert.Equal("{\n\t\tint a;\n#define C\n}\n", plan.NewText);
    }

    /// <summary>原样能命中就不剥：文件里真有那层缩进</summary>
    [Fact]
    public void Edit_KeepsLeadingTab_WhenItMatchesAsIs()
    {
        FileEditPlan plan = FileEditPlanner.Plan("\t\tint a;\n", "a.cpp",
            [new FileEdit { OldString = "\t\tint a;", NewString = "\t\tint b;" }]);

        Assert.True(plan.Succeeded, plan.Error);
        Assert.False(plan.StrippedLinePrefixes);
        Assert.Equal("\t\tint b;\n", plan.NewText);
    }
}
