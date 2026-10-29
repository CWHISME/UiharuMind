/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Shared.Controls;
using Xunit;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 提示浮层里的文案，不能被悄悄吃掉行。
///
/// 提示框的宽度是按「文字自然宽度 + 内边距」定的，排版时拿到的那点宽度常比测量时窄
/// （内边距、边框、弹窗尺寸按设备像素取整都会磨掉一点）。<see cref="TextBlock"/> 在
/// <c>ArrangeOverride</c> 里是丢掉旧排版、<b>按拿到的尺寸重建一份</b>，又 <c>ClipToBounds</c>：
/// 宽度窄一点就多折出一行，而高度还是按测量时那份算的——多出来的行整行消失，没有省略号。
/// 单行 tip（「167.3k / 1M」）会只剩前半句，长 tip 会少最后一句。
///
/// <see cref="TipText"/> 排版时沿用测量时的宽度与较大的高度，所以两个方向都不会变。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class TipTextLayoutTests
{
    /// <summary>Semi 的提示框可用宽度：320 的 MaxWidth 减去左右各 12 的内边距</summary>
    private const double TipWidth = 294;

    /// <summary>弹窗比内容窄的那点：内边距 + 边框 + 像素取整，量级是几个 DIP</summary>
    private const double Narrower = 8;

    private const string LongTip =
        "会话累计 93.9% · 4.4M / 4.7M\n" +
        "本轮 82.9% · 645.1k / 777.8k\n" +
        "最近一次 97.7% · 131.1k / 134.1k\n" +
        "\n" +
        "命中率 = 命中输入 / 服务端报的输入。正文那一行是会话累计，分子分母来自同一批请求。\n" +
        "单发的那一档按构造就接近全命中——一轮里上下文单调增长，最后一发的前缀正好是上一发的全部——\n" +
        "拿它当命中率会虚高，本轮那一档同理。";

    /// <summary>裸 TextBlock 按弹层给的宽度重建排版：窄一点就多折一行，多出来的行没有地方画</summary>
    [Fact]
    public void PlainTextBlock_RebuildsAtTheNarrowerWidth()
    {
        HeadlessUi.Run(() =>
        {
            TextBlock plain = new() { Text = LongTip, TextWrapping = TextWrapping.Wrap };
            Size natural = Measure(plain);
            Assert.True(plain.TextLayout.TextLines.Count > 3);

            Arrange(plain, natural.Width - Narrower, natural.Height);

            // 排版被换成了窄的宽度：多折出来的行没有地方画（高度还是测量时那份）
            Assert.Equal(natural.Width - Narrower, plain.TextLayout.MaxWidth, 3);
        });
    }

    /// <summary>同样的窄框，<see cref="TipText"/> 仍按测量时的宽度排版，一行不少</summary>
    [Fact]
    public void TipText_KeepsTheMeasureWidth()
    {
        HeadlessUi.Run(() =>
        {
            TipText tip = new() { Text = LongTip };
            Size natural = Measure(tip);
            double measureWidth = tip.TextLayout.MaxWidth;
            int lines = tip.TextLayout.TextLines.Count;

            Arrange(tip, natural.Width - Narrower, natural.Height);

            Assert.Equal(measureWidth, tip.TextLayout.MaxWidth, 3);
            Assert.Equal(lines, tip.TextLayout.TextLines.Count);
        });
    }

    /// <summary>长 tip：客户区矮一点时，最后一行也不能少</summary>
    [Fact]
    public void TipText_KeepsTheLastLine()
    {
        HeadlessUi.Run(() =>
        {
            TipText tip = new() { Text = LongTip };
            Size natural = Measure(tip);
            double naturalLayout = tip.TextLayout.Height;
            int lines = tip.TextLayout.TextLines.Count;

            Arrange(tip, natural.Width, natural.Height - 1);

            Assert.Equal(naturalLayout, tip.TextLayout.Height, 3);
            Assert.Equal(lines, tip.TextLayout.TextLines.Count);
        });
    }

    /// <summary>单行 tip（占用值）不能因为窄了一点就折成两行、只显示前半句</summary>
    [Fact]
    public void TipText_KeepsASingleLineTipOnOneLine()
    {
        HeadlessUi.Run(() =>
        {
            TipText tip = new() { Text = "167.3k / 1M" };
            Size natural = Measure(tip);
            Assert.Equal(1, tip.TextLayout.TextLines.Count);

            Arrange(tip, natural.Width - Narrower, natural.Height);

            Assert.Equal(1, tip.TextLayout.TextLines.Count);
        });
    }

    /// <summary>两行的提示（本轮/累计）也不能少一行——面板上「累计消耗」那一格就是它</summary>
    [Fact]
    public void TipText_KeepsBothLinesOfATwoLineTip()
    {
        HeadlessUi.Run(() =>
        {
            TipText tip = new() { Text = "本轮 ↑ 3.7M ↓ 12.9k\n累计 ↑ 8M ↓ 52k" };
            Size natural = Measure(tip);

            Arrange(tip, natural.Width - Narrower, natural.Height - 1);

            Assert.Equal(2, tip.TextLayout.TextLines.Count);
        });
    }

    /// <summary>客户区更宽松时也不该多排：多出来的地方留在框里</summary>
    [Fact]
    public void TipText_DoesNotStretchIntoSpareRoom()
    {
        HeadlessUi.Run(() =>
        {
            TipText tip = new() { Text = LongTip };
            Size natural = Measure(tip);
            double naturalLayout = tip.TextLayout.Height;
            int lines = tip.TextLayout.TextLines.Count;

            Arrange(tip, natural.Width, natural.Height + 40);

            Assert.Equal(naturalLayout, tip.TextLayout.Height, 3);
            Assert.Equal(lines, tip.TextLayout.TextLines.Count);
        });
    }

    /// <summary>控件内容的提示原样显示（别被当成数据再套一层模板，屏幕上会变成类名）</summary>
    [Fact]
    public void ControlContentTip_RendersAsItself()
    {
        HeadlessUi.Run(() =>
        {
            TipText content = new() { Text = "82" };
            ToolTip tip = new() { Content = content };
            Window window = new() { Width = 400, Height = 300, Content = tip };
            window.Show();
            window.UpdateLayout();

            TextBlock inner = tip.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.Same(content, inner);
        });
    }

    /// <summary>图标按钮的 ToolTips 也走 TipText（字符串会被包成裸 TextBlock，见类注释）</summary>
    [Fact]
    public void IconButtonToolTip_IsTipText()
    {
        HeadlessUi.Run(() =>
        {
            ThemedIconButton button = new() { ToolTips = "82" };

            Assert.IsType<TipText>(ToolTip.GetTip(button));
        });
    }

    private static Size Measure(TextBlock block)
    {
        // 与弹层根一致：宽度受限于提示框宽度，高度不限
        block.Measure(new Size(TipWidth, double.PositiveInfinity));
        return block.DesiredSize;
    }

    private static void Arrange(TextBlock block, double width, double height) =>
        block.Arrange(new Rect(0, 0, width, height));
}
