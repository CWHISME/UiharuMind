using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Shared.Controls.TextRendering;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// DiffTextBlock 的纯逻辑两半：BuildInlines（行间 LineBreak、增/删行前景）
/// 与 BuildDecorations（行级整块背景：增绿/删红/context 无；字符偏移与 Inlines.Text 拼接规则一致）。
/// 背景的几何绘制由 DecoratedTextBlock 按 TextLayout 在渲染期完成，不在本测试范围
/// </summary>
public class DiffTextBlockTests
{
    [Fact]
    public void BuildInlines_JoinsEveryLineAsRunWithLineBreakBetween()
    {
        var lines = new[]
        {
            DiffLineView.Context("ctx"),
            DiffLineView.Added("old"),
            DiffLineView.Removed("new"),
        };

        var inlines = DiffTextBlock.BuildInlines(lines, null, null);

        var runs = inlines.OfType<Run>().ToArray();
        var breaks = inlines.OfType<LineBreak>().ToArray();
        Assert.Equal(3, runs.Length);
        Assert.Equal(2, breaks.Length);
        Assert.Equal(" ctx", runs[0].Text);
        Assert.Equal("+old", runs[1].Text);
        Assert.Equal("-new", runs[2].Text);
        // Inlines.Text 拼接规则：行间一个换行（BuildDecorations 的偏移必须与此一致）
        Assert.Equal(" ctx\n+old\n-new", inlines.Text);
    }

    [Fact]
    public void BuildInlines_ColorsAddedAndRemovedForeground_LeavesContextPlain()
    {
        var lines = new[]
        {
            DiffLineView.Context("ctx"),
            DiffLineView.Added("old"),
            DiffLineView.Removed("new"),
        };
        var greenFg = new SolidColorBrush(Colors.Green);
        var redFg = new SolidColorBrush(Colors.Red);

        var inlines = DiffTextBlock.BuildInlines(lines, greenFg, redFg);
        var runs = inlines.OfType<Run>().ToArray();

        // context：不用增删配色（Foreground 默认非 null，故用 NotSame）
        Assert.NotSame(greenFg, runs[0].Foreground);
        Assert.NotSame(redFg, runs[0].Foreground);

        // added：前景绿；removed：前景红
        Assert.Same(greenFg, runs[1].Foreground);
        Assert.Same(redFg, runs[2].Foreground);
    }

    [Fact]
    public void BuildDecorations_AddedGreen_RemovedRed_ContextNone_OffsetsMatchText()
    {
        var lines = new[]
        {
            DiffLineView.Context("ctx"),
            DiffLineView.Added("old"),
            DiffLineView.Removed("new"),
        };
        var greenBg = new SolidColorBrush(Colors.LightGreen);
        var redBg = new SolidColorBrush(Colors.LightCoral);

        var decorations = DiffTextBlock.BuildDecorations(lines, greenBg, redBg);

        // 文本 = " ctx\n+old\n-new"；added 行 "+old" 起点 5、removed 行 "-new" 起点 10
        Assert.Equal(2, decorations.Count);
        Assert.Equal(5, decorations[0].Start);
        Assert.Equal(4, decorations[0].Length);
        Assert.Same(greenBg, decorations[0].Background);
        Assert.True(decorations[0].FillRowWidth);
        Assert.Equal(10, decorations[1].Start);
        Assert.Equal(4, decorations[1].Length);
        Assert.Same(redBg, decorations[1].Background);
    }

    [Fact]
    public void BuildDecorations_EmptyAddedLine_StillProducesRowSpan()
    {
        // 空行（Prefix="+" Text=""）在文本里是 1 个字符，背景仍应覆盖它
        var lines = new[] { DiffLineView.Added("") };
        var greenBg = new SolidColorBrush(Colors.LightGreen);

        var decorations = DiffTextBlock.BuildDecorations(lines, greenBg, null);

        var span = Assert.Single(decorations);
        Assert.Equal(0, span.Start);
        Assert.Equal(1, span.Length);
    }

    [Fact]
    public void BuildInlines_EmptyLines_ReturnsEmptyCollection()
    {
        var inlines = DiffTextBlock.BuildInlines([], null, null);

        Assert.Empty(inlines);
    }

    [Fact]
    public void StyleKey_StaysSelectableTextBlock_SelectsThemeAndCopyMenu()
    {
        // Avalonia 类型选择器是精确匹配（control.StyleKey == TargetType）：
        // 不覆写 StyleKeyOverride，SelectableTextBlock 的 ControlTheme
        // （SelectionBrush、右键复制菜单）与 mono/muted 类样式都命中不了——
        // 表现为拖选无高亮、无复制菜单、等宽字体丢失。与 SyntaxHighlightTextBlock 同口径
        Assert.Equal(typeof(SelectableTextBlock), new DiffTextBlock().StyleKey);
        Assert.Equal(typeof(SelectableTextBlock), new DecoratedTextBlock().StyleKey);
    }
}