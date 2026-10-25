using UiharuMind.Features.Conversation.Composer;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// /compact 命令的输入解析：整行精确匹配、忽略首尾空白与大小写，
/// 命令后跟的文字作为用户附加的额外指示取出。
/// </summary>
public class CommandPaletteCompactTests
{
    [Theory]
    [InlineData("/compact")]
    [InlineData(" /compact ")]
    [InlineData("/Compact")]
    [InlineData("/compact 别忘了记录临时结论")]
    public void TryParseCompact_AcceptsExactCommand_AndCommandTrailingText(string text)
    {
        Assert.True(CommandPaletteViewData.TryParseCompact(text, out _));
    }

    [Fact]
    public void TryParseCompact_TakesTrailingTextAsExtraInstructions()
    {
        Assert.True(CommandPaletteViewData.TryParseCompact("/compact 别忘了记录临时结论", out string? extra));

        Assert.Equal("别忘了记录临时结论", extra);
    }

    [Fact]
    public void TryParseCompact_WhitespaceOnlyArgument_YieldsNoExtra()
    {
        Assert.True(CommandPaletteViewData.TryParseCompact("/compact   ", out string? extra));

        Assert.Null(extra);
    }

    [Fact]
    public void TryParseCompact_RejectsTextThatMerelyContainsTheWord()
    {
        Assert.False(CommandPaletteViewData.TryParseCompact("帮我 compact 一下这段", out _));
    }

    [Fact]
    public void TryParseCompact_DoesNotSwallowWordsThatStartWithTheCommand()
    {
        Assert.False(CommandPaletteViewData.TryParseCompact("/compactness", out _));
    }
}

/// <summary>
/// /clear 命令的输入解析：整行精确匹配、忽略首尾空白与大小写；
/// 命令后跟任何文字都不算——/clear 不带参数，防止吞掉想聊这个单词的普通消息
/// </summary>
public class CommandPaletteClearTests
{
    [Theory]
    [InlineData("/clear")]
    [InlineData(" /clear ")]
    [InlineData("/Clear")]
    public void TryParseClear_AcceptsExactCommand(string text)
    {
        Assert.True(CommandPaletteViewData.TryParseClear(text));
    }

    [Fact]
    public void TryParseClear_RejectsTrailingText()
    {
        Assert.False(CommandPaletteViewData.TryParseClear("/clear 记得清干净"));
    }

    [Fact]
    public void TryParseClear_RejectsWordsThatStartWithTheCommand()
    {
        Assert.False(CommandPaletteViewData.TryParseClear("/clearly"));
    }
}