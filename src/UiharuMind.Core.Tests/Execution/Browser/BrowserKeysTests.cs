using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 按键：别名统一成 Puppeteer 的键名；会不会往焦点里写字决定了密码框拦不拦——回车、Tab 放行，打字、粘贴拦下，
/// 认不出的（键码名 KeyA、Digit1）一律当会写
/// </summary>
public class BrowserKeysTests
{
    [Theory]
    [InlineData("ctrl+a", new[] { "Control", "a" })]
    [InlineData("Cmd + Enter", new[] { "Meta", "Enter" })]
    [InlineData("esc", new[] { "Escape" })]
    [InlineData("pgdn", new[] { "PageDown" })]
    [InlineData("", new string[0])]
    public void Aliases_AreNormalized(string keys, string[] expected)
    {
        Assert.Equal(expected, BrowserKeys.Parse(keys));
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("Shift+A", true)]
    [InlineData("Alt+e", true)]
    [InlineData("Control+V", true)]
    [InlineData("Meta+v", true)]
    [InlineData("Enter", false)]
    [InlineData("Tab", false)]
    [InlineData("Backspace", false)]
    [InlineData("Control+A", false)]
    [InlineData("Meta+Enter", false)]
    [InlineData("KeyA", true)]
    [InlineData("Digit1", true)]
    [InlineData("Space", true)]
    [InlineData("Shift+Insert", true)]
    [InlineData("Control+KeyV", true)]
    [InlineData("F5", false)]
    [InlineData("ArrowDown", false)]
    public void TypesText_TellsTypingAndPastingFromNavigationKeys(string keys, bool expected)
    {
        Assert.Equal(expected, BrowserKeys.TypesText(BrowserKeys.Parse(keys)));
    }
}
