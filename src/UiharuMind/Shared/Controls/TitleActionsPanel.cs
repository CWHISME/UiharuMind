using System;
using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 卡头：第一个子项是标题（左），第二个是操作按钮（右）。
/// 放得下时同一行，标题吃剩下的宽度；标题被挤到 <see cref="MinTitleWidth"/> 以下时按钮换到下一行左对齐，
/// 不让标题塌成零、也不让溢出的徽标压到按钮底下
/// </summary>
public class TitleActionsPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TitleActionsPanel, double>(nameof(Spacing), 8);

    public static readonly StyledProperty<double> MinTitleWidthProperty =
        AvaloniaProperty.Register<TitleActionsPanel, double>(nameof(MinTitleWidth), 240);

    private bool _wrapped; //这一轮量下来按钮换了行

    static TitleActionsPanel()
    {
        AffectsMeasure<TitleActionsPanel>(SpacingProperty, MinTitleWidthProperty);
    }

    /// <summary>
    /// 标题与按钮之间的间距（同行为横向，换行为纵向）
    /// </summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// 同一行时标题至少留多宽；标题本身更窄时按它自己的宽度算
    /// </summary>
    public double MinTitleWidth
    {
        get => GetValue(MinTitleWidthProperty);
        set => SetValue(MinTitleWidthProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count < 2) return base.MeasureOverride(availableSize);
        Control title = Children[0];
        Control actions = Children[1];

        actions.Measure(Size.Infinity);
        title.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        Size actionsSize = actions.DesiredSize;
        double titleNeeds = Math.Min(title.DesiredSize.Width, MinTitleWidth);
        double titleRoom = availableSize.Width - actionsSize.Width - Spacing;

        _wrapped = titleRoom < titleNeeds;
        if (_wrapped)
        {
            title.Measure(availableSize);
            return new Size(Math.Max(title.DesiredSize.Width, actionsSize.Width),
                title.DesiredSize.Height + Spacing + actionsSize.Height);
        }

        title.Measure(new Size(Math.Max(0, titleRoom), availableSize.Height));
        return new Size(title.DesiredSize.Width + Spacing + actionsSize.Width,
            Math.Max(title.DesiredSize.Height, actionsSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count < 2) return base.ArrangeOverride(finalSize);
        Control title = Children[0];
        Control actions = Children[1];
        Size actionsSize = actions.DesiredSize;

        if (_wrapped)
        {
            title.Arrange(new Rect(0, 0, finalSize.Width, title.DesiredSize.Height));
            actions.Arrange(new Rect(0, title.DesiredSize.Height + Spacing, actionsSize.Width, actionsSize.Height));
            return finalSize;
        }

        double titleWidth = Math.Max(0, finalSize.Width - actionsSize.Width - Spacing);
        title.Arrange(new Rect(0, 0, titleWidth, finalSize.Height));
        actions.Arrange(new Rect(finalSize.Width - actionsSize.Width, 0, actionsSize.Width, finalSize.Height));
        return finalSize;
    }
}
