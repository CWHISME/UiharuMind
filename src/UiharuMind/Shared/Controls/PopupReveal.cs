using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 弹出层（下拉框、菜单、Flyout、提示）的出现动画：淡入加轻微下滑。
///
/// 不用 Semi 自带的 <c>SemiPopupAnimations</c>：它是关键帧动画，时钟从样式生效那一刻起算，
/// 而首次展开要建弹出层、套模板、生成下拉项，这段耗时（实测约 80ms）就把 100ms 的动画吃光了，
/// 第一帧画出来时已经播完，只有第二次展开才看得见；且它只做 0.98 缩放，即使完整播放也几乎看不出。
///
/// 这里挂载时同步压到隐藏态（首帧不会以满透明度闪一下），推迟到首次布局完成之后才开始计时。
/// 只做出现，不做消失：弹出层关闭时宿主随即移除，消失动画没有可播放的地方。
/// 由 Assets/Themes/CustomPopupStyle.axaml 挂到所有弹出层的内容承载层上。
/// </summary>
public static class PopupReveal
{
    private const int DurationMilliseconds = 120;
    private const double HiddenOffsetY = -6;

    /// <summary>是否启用出现动画</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<LayoutTransformControl, bool>("IsEnabled", typeof(PopupReveal));

    private static readonly Animation RevealAnimation = CreateRevealAnimation();

    static PopupReveal()
    {
        IsEnabledProperty.Changed.AddClassHandler<LayoutTransformControl>(OnIsEnabledChanged);
    }

    /// <summary>设置是否启用出现动画</summary>
    public static void SetIsEnabled(LayoutTransformControl element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    /// <summary>获取是否启用出现动画</summary>
    public static bool GetIsEnabled(LayoutTransformControl element) => element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(LayoutTransformControl target, AvaloniaPropertyChangedEventArgs e)
    {
        target.AttachedToVisualTree -= OnAttached;
        if (e.NewValue is not true) return;

        target.AttachedToVisualTree += OnAttached;
        if (TopLevel.GetTopLevel(target) != null) Begin(target);
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is LayoutTransformControl target) Begin(target);
    }

    private static void Begin(LayoutTransformControl target)
    {
        target.Opacity = 0;
        // 首次布局之后才起播：此前的建树、套模板、排版耗时不计入动画时长
        Dispatcher.UIThread.Post(() => _ = Play(target), DispatcherPriority.Loaded);
    }

    private static async System.Threading.Tasks.Task Play(LayoutTransformControl target)
    {
        try
        {
            await RevealAnimation.RunAsync(target);
        }
        finally
        {
            // 动画层撤掉后会回落到本地值（0），必须显式放回 1，否则弹出层播完就消失
            target.Opacity = 1;
            target.RenderTransform = null;
        }
    }

    private static Animation CreateRevealAnimation()
    {
        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(DurationMilliseconds),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(TranslateTransform.YProperty, HiddenOffsetY),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(TranslateTransform.YProperty, 0d),
                    },
                },
            },
        };
    }
}
