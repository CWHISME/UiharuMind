using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 横排、字号不一的几段西文（工具名 + 路径摘要）按首行基线对齐，整组在行内竖直居中。
/// 西文并排时眼睛对的是基线，各自居中会差出零点几到一像素；中文或带图标的行仍用 CenteredStackPanel。
/// 最后一个子项吃掉剩余宽度，方便它自己做截断
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
        Control[] children = Children.Where(x => x.IsVisible).ToArray();
        double width = 0;
        double ascent = 0;
        double descent = 0;
        for (int i = 0; i < children.Length; i++)
        {
            if (i > 0) width += Spacing;
            children[i].Measure(new Size(Math.Max(0, availableSize.Width - width), availableSize.Height));
            width += children[i].DesiredSize.Width;
            double baseline = BaselineOf(children[i]);
            ascent = Math.Max(ascent, baseline);
            descent = Math.Max(descent, children[i].DesiredSize.Height - baseline);
        }

        return new Size(width, ascent + descent);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Control[] children = Children.Where(x => x.IsVisible).ToArray();
        double ascent = 0;
        double descent = 0;
        foreach (Control child in children)
        {
            double baseline = BaselineOf(child);
            ascent = Math.Max(ascent, baseline);
            descent = Math.Max(descent, child.DesiredSize.Height - baseline);
        }

        double scale = LayoutHelper.GetLayoutScale(this);
        double groupTop = (finalSize.Height - ascent - descent) / 2;
        double x = 0;
        for (int i = 0; i < children.Length; i++)
        {
            Control child = children[i];
            Size size = child.DesiredSize;
            double width = i == children.Length - 1 ? Math.Max(0, finalSize.Width - x) : size.Width;
            double y = LayoutHelper.RoundLayoutValue(groupTop + ascent - BaselineOf(child), scale);
            child.Arrange(new Rect(x, y, width, size.Height));
            x += width + Spacing;
        }

        return finalSize;
    }

    // 子项盒顶到首行基线的距离；不是文字的按底边算
    private static double BaselineOf(Control child)
    {
        if (child is TextBlock text && text.TextLayout.TextLines.Count > 0)
            return child.Margin.Top + text.Padding.Top + text.TextLayout.TextLines[0].Baseline;
        return child.DesiredSize.Height;
    }
}
