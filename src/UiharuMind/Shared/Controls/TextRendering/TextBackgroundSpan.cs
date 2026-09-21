using Avalonia;
using Avalonia.Media;

namespace UiharuMind.Shared.Controls.TextRendering;

/// <summary>
/// 文本上的一个背景装饰段：字符区间（相对 <c>Inlines.Text</c> / TextLayout 文本源）+ 背景。
/// 供 <see cref="DecoratedTextBlock"/> 在渲染期自绘。
///
/// 区间语义与 LiveMarkdown 的 CodeInlineSpan 一致：按<b>字符跨度</b>定位，
/// 空行/换行天然正确处理，不会像「行索引 ↔ TextLine 索引」那样错位。
/// </summary>
public sealed class TextBackgroundSpan
{
    /// <summary>文本起点（字符，相对 Inlines.Text）</summary>
    public int Start { get; init; }

    /// <summary>文本长度（字符）</summary>
    public int Length { get; init; }

    /// <summary>背景笔；null 时该段不画</summary>
    public IBrush? Background { get; init; }

    /// <summary>铺满整行宽（diff 增删行用）；false 时仅覆盖文字矩形 + <see cref="Padding"/></summary>
    public bool FillRowWidth { get; init; }

    /// <summary><see cref="FillRowWidth"/> 为 false 时背景相对文字四周的扩边</summary>
    public Thickness Padding { get; init; }

    /// <summary>背景圆角半径；0 表示直角</summary>
    public double Radius { get; init; }

    /// <summary>区间终点（不含）</summary>
    public int End => Start + Length;
}