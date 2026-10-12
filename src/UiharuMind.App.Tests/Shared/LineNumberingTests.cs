using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Shared;

/// <summary>行号口径：片段从原文件中间读起时偏移，附注行不编号，default 等同整份</summary>
public class LineNumberingTests
{
    [Fact]
    public void Fragment_ShiftsNumbers_AndLeavesAppendixUnnumbered()
    {
        var numbering = new LineNumbering(60, 2);

        Assert.Equal(60, numbering.NumberOf(1));
        Assert.Equal(61, numbering.NumberOf(2));
        Assert.Null(numbering.NumberOf(3)); //截断附注、大纲
        Assert.False(numbering.IsWhole);
    }

    [Fact]
    public void Default_IsWhole()
    {
        LineNumbering numbering = default;

        Assert.True(numbering.IsWhole);
        Assert.Equal(5, numbering.NumberOf(5));
    }
}
