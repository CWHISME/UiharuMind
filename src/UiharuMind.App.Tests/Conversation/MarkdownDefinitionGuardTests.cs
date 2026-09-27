using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 行首 <c>[名字]: 正文</c> 会被 markdown 当链接引用定义整行吞掉（气泡空白、复制有字）。
/// </summary>
public class MarkdownDefinitionGuardTests
{
    [Theory]
    [InlineData("[白井黑子]: 先别急着客气。", "\\[白井黑子]: 先别急着客气。")]
    [InlineData("开头\n\n[Alice]: 第二段", "开头\n\n\\[Alice]: 第二段")]
    [InlineData("  [Alice]：不是英文冒号", "  [Alice]：不是英文冒号")] //全角冒号本来就不是定义
    public void EscapesLinesThatWouldBeSwallowed(string input, string expected)
    {
        Assert.Equal(expected, MarkdownDefinitionGuard.Escape(input));
    }

    [Theory]
    [InlineData("[1]: https://example.com")]
    [InlineData("[doc]: ./readme.md")]
    [InlineData("[a]: <x y>")]
    [InlineData("[top]: #section")]
    [InlineData("普通正文 [Alice]: 在行中间")]
    [InlineData("```\n[Alice]: 代码里的字面值\n```")]
    public void LeavesRealReferencesAndCodeAlone(string input)
    {
        Assert.Same(input, MarkdownDefinitionGuard.Escape(input));
    }
}
