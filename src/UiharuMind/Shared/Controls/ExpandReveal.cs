using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 折叠内容的展开容器：<see cref="IsOpen"/> 切换时高度逐帧渐进，替代直接切 <c>IsVisible</c>。
///
/// 不用「一次性显示」的原因：会话流底部展开卡片时 Extent 一帧内暴涨，
/// <see cref="ScrollViewerAutoScrollHolder"/> 随后补底，看起来是先弹一下再定住。
/// 高度渐进后 Extent 逐帧增长；而宿主在底部时，本控件在<b>同一帧内</b>把偏移贴回底部——
/// 不能交给 <see cref="ScrollViewerAutoScrollHolder"/>：它在 Loaded 优先级补底，晚于该帧渲染，
/// 每一帧都是「内容先长出来、偏移滞后一帧再追上」，逐帧节奏一乱就是抖。
///
/// 内容始终按完整高度排版、外框按进度裁剪，所以展开过程中内容不会重排。
/// 收起且动画结束后控件自身 <c>IsVisible</c> 置 false（折叠的重内容不参与布局与渲染），
/// 因此宿主不要再绑定它的 <c>IsVisible</c>。
/// </summary>
public class ExpandReveal : Decorator
{
    private const int DurationMilliseconds = 120;
    private const double BottomTolerance = 4.0; //离底多少像素以内算"在底部"，与 ScrollViewerAutoScrollHolder 同一口径

    /// <summary>是否展开</summary>
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ExpandReveal, bool>(nameof(IsOpen));

    private static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<ExpandReveal, double>(nameof(Progress));

    /// <summary>是否展开</summary>
    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    static ExpandReveal()
    {
        ClipToBoundsProperty.OverrideDefaultValue<ExpandReveal>(true);
        AffectsMeasure<ExpandReveal>(ProgressProperty);
        IsOpenProperty.Changed.AddClassHandler<ExpandReveal>((reveal, _) => reveal.OnIsOpenChanged());
        ProgressProperty.Changed.AddClassHandler<ExpandReveal>((reveal, _) => reveal.OnProgressChanged());
    }

    public ExpandReveal()
    {
        // 首次挂载前的赋值不会走过渡(Avalonia 在附加到可视树后才启用 Transitions)，
        // 所以初始即展开的卡片不会在载入时播一遍动画
        Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = ProgressProperty,
                Duration = TimeSpan.FromMilliseconds(DurationMilliseconds),
                Easing = new CubicEaseOut(),
            },
        };
        IsVisible = false;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child == null) return default;

        Child.Measure(availableSize.WithHeight(double.PositiveInfinity));
        return new Size(Child.DesiredSize.Width, Child.DesiredSize.Height * Math.Clamp(Progress, 0, 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, Child.DesiredSize.Height));
        return finalSize;
    }

    private void OnIsOpenChanged()
    {
        if (IsOpen) IsVisible = true;
        Progress = IsOpen ? 1 : 0;
    }

    private void OnProgressChanged()
    {
        IsVisible = IsOpen || Progress > 0;
        if (IsOpen) KeepBottomPinned();
    }

    private void KeepBottomPinned()
    {
        ScrollViewer? scroll = this.FindAncestorOfType<ScrollViewer>();
        if (scroll == null || scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y > BottomTolerance) return;

        // 先同步排一次版让 Extent 更新到本帧的高度：Offset 会按旧 Extent 钳制，直接写会被截在旧的底部
        UpdateLayout();
        scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
    }
}
