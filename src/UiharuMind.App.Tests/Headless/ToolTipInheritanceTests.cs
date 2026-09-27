using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 提示框不能继承宿主的「单行 + 省略号」。MaxLines / TextTrimming / TextWrapping 是可继承属性，
/// ToolTip 的逻辑父级是挂它的控件——窄栏里截断的描述挂上全文提示，曾经提示本身也只剩一行，
/// 宽度取决于屏幕右侧剩多少空间（拉宽右栏反而看得全）。全局样式在 CustomFontStyle.axaml
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ToolTipInheritanceTests
{
    [Fact]
    public void ToolTip_OnTrimmedTextBlock_DoesNotInheritSingleLine() => HeadlessUi.Run(() =>
    {
        string text = string.Concat(Enumerable.Repeat("很长的角色描述，窄栏里放不下。", 20));
        ToolTip tip = new() { Content = text };
        TextBlock block = new()
        {
            Text = text, Width = 80, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTip.SetTip(block, tip);
        Window window = new() { Width = 400, Height = 200, Content = block };
        window.Show();

        ToolTip.SetIsOpen(block, true);
        window.UpdateLayout();
        TextBlock presented = tip.GetLogicalDescendants().OfType<TextBlock>().First();
        presented.UpdateLayout();

        Assert.Equal(0, tip.GetValue(TextBlock.MaxLinesProperty));
        Assert.Equal(TextTrimming.None, tip.GetValue(TextBlock.TextTrimmingProperty));
        Assert.True(presented.TextLayout.TextLines.Count > 1, "提示框应换行显示全文，而不是截成一行");
        ToolTip.SetIsOpen(block, false);
        window.Close();
    });
}
