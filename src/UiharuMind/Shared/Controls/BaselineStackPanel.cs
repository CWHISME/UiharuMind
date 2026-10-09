using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 横排、字号不一的几段文字按基线对齐（名字 + 时间、标题 + 小字说明）。
/// StackPanel 只会按盒子顶/中/底对齐：大小字盒子居中时小字的基线偏上，看着「小号的往上飘」。
/// 这里 TextBlock 一律落在同一条基线上；其余子项（圆点、图标、徽标）按整行竖直居中
/// </summary>
public class BaselineStackPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<BaselineStackPanel, double>(nameof(Spacing));

    static BaselineStackPanel()
    {
        AffectsMeasure<BaselineStackPanel>(SpacingProperty);
    }

    /// <summary>
    /// 子项之间的间距
    /// </summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        double ascent = 0;
        double descent = 0;
        double others = 0;
        int visible = 0;
        foreach (Control child in Children.Where(x => x.IsVisible))
        {
            child.Measure(new Size(Math.Max(0, availableSize.Width - width), availableSize.Height));
            width += child.DesiredSize.Width;
            visible++;
            if (Baseline(child) is { } baseline)
            {
                ascent = Math.Max(ascent, baseline);
                descent = Math.Max(descent, child.DesiredSize.Height - baseline);
            }
            else
            {
                others = Math.Max(others, child.DesiredSize.Height);
            }
        }

        width += Spacing * Math.Max(0, visible - 1);
        return new Size(width, Math.Max(ascent + descent, others));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double ascent = Children.Where(x => x.IsVisible).Select(Baseline).Max() ?? 0;
        double descent = Children.Where(x => x.IsVisible)
            .Select(x => Baseline(x) is { } b ? x.DesiredSize.Height - b : (double?)null).Max() ?? 0;
        // 文字整体在行里居中，再各自落到同一条基线上。布局取整会把每个子项的顶各自吸到像素上，
        // 所以先把基线吸到像素格，再按它倒推各子项的顶（取整后只剩各字号基线自身的小数部分，不到半像素）
        double scale = LayoutHelper.GetLayoutScale(this);
        double line = LayoutHelper.RoundLayoutValue(Math.Max(0, (finalSize.Height - ascent - descent) / 2) + ascent, scale);
        double x = 0;
        foreach (Control child in Children.Where(c => c.IsVisible))
        {
            Size size = child.DesiredSize;
            double y = Baseline(child) is { } baseline
                ? LayoutHelper.RoundLayoutValue(line - baseline, scale)
                : (finalSize.Height - size.Height) / 2;
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
        }

        return finalSize;
    }

    // 第一行基线到子项盒子顶的距离（含外边距与内边距）；不是文字的返回 null
    private static double? Baseline(Control child) =>
        child is TextBlock { TextLayout.TextLines.Count: > 0 } text
            ? text.Margin.Top + text.Padding.Top + text.TextLayout.TextLines[0].Baseline
            : null;
}
