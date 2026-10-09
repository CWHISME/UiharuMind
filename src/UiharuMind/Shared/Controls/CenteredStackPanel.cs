using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 横排、字号不一的几段文字（名字 + 时间、标题 + 小字说明）与圆点、图标、徽标一律按整行竖直居中。
/// 横排 StackPanel 默认把子项顶对齐，大小字并排时小字整个往上飘；按基线对齐又会让小字的视觉中心偏下。
/// 各段文字的墨迹已由全局文字下移（TextOpticalOffsetConverter）居中在各自行盒里，行盒居中即视觉中心对齐
/// </summary>
public class CenteredStackPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<CenteredStackPanel, double>(nameof(Spacing));

    static CenteredStackPanel()
    {
        AffectsMeasure<CenteredStackPanel>(SpacingProperty);
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
        double height = 0;
        int visible = 0;
        foreach (Control child in Children.Where(x => x.IsVisible))
        {
            child.Measure(new Size(Math.Max(0, availableSize.Width - width), availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
            visible++;
        }

        return new Size(width + Spacing * Math.Max(0, visible - 1), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // 布局取整会把子项的顶吸到像素上，这里先吸好，免得各自往不同方向取整差出一像素
        double scale = LayoutHelper.GetLayoutScale(this);
        double x = 0;
        foreach (Control child in Children.Where(c => c.IsVisible))
        {
            Size size = child.DesiredSize;
            double y = LayoutHelper.RoundLayoutValue((finalSize.Height - size.Height) / 2, scale);
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
        }

        return finalSize;
    }
}
