/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 透明度的外部落点。托管 <c>Opacity</c> 要等下一次渲染才生效，赶不上原生的移动与显示
/// （窗口一挪，合成器会把上一帧的内容直接贴到新位置），需要立即生效的通道时用它顶上，
/// 例如 macOS 的窗口 alpha。自己记住当前值，供动画从中途接着补。
/// </summary>
public sealed class OpacityChannel
{
    private readonly Action<double> _apply;

    /// <summary>最后落下去的透明度</summary>
    public double Value { get; private set; } = 1;

    /// <param name="apply">怎么把透明度落下去</param>
    public OpacityChannel(Action<double> apply)
    {
        _apply = apply;
    }

    /// <summary>
    /// 落一次透明度
    /// </summary>
    /// <param name="opacity">0~1</param>
    public void Apply(double opacity)
    {
        Value = opacity;
        _apply(opacity);
    }
}

public static class UiAnimationUtils
{
    private const double HorizontalRevealHiddenOffset = -10;
    private const double VerticalRevealHiddenOffset = -8;
    private const double NotificationHiddenOffset = 16;
    private const int ControlTransitionMilliseconds = 120;
    private const int VerticalRevealMilliseconds = 200;
    private const int NotificationTransitionMilliseconds = 180;

    private static Dictionary<Visual, CancellationTokenSource> _animationCts =
        new Dictionary<Visual, CancellationTokenSource>();


    private static IPageTransition? _horizontalSlideTransition;

    private static IPageTransition HorizontalSlideTransition
    {
        get { return _horizontalSlideTransition ??= CreateCompositeTransition(); }
    }

    private static IPageTransition? _alphaTransition;

    private static IPageTransition AlphaTransition
    {
        get { return _alphaTransition ??= CreateAlphaTransition(); }
    }

    /// <summary>
    /// 透明度动画
    /// </summary>
    /// <param name="visual"></param>
    /// <param name="isShowed"></param>
    /// <param name="onCompleted"></param>
    public static void PlayAlphaTransitionAnimation(Visual? visual, bool isShowed,
        Action? onCompleted = null)
    {
        PlayTransitionAnimation(AlphaTransition, visual, isShowed, onCompleted);
    }

    /// <summary>
    /// 从右到左滑动动画
    /// </summary>
    /// <param name="visual"></param>
    /// <param name="isShowed"></param>
    /// <param name="onCompleted"></param>
    public static void PlayRightToLeftTransitionAnimation(Visual? visual, bool isShowed,
        Action? onCompleted = null)
    {
        if (visual is Control control)
        {
            PlayHorizontalRevealAnimation(control, isShowed, onCompleted);
            return;
        }

        PlayTransitionAnimation(HorizontalSlideTransition, visual, isShowed, onCompleted);
    }

    public static void PrepareRightToLeftTransitionTarget(Control? control)
    {
        if (control == null) return;
        control.Opacity = 0;
        control.IsVisible = true;
        control.IsHitTestVisible = false;
        control.RenderTransform = new TranslateTransform(HorizontalRevealHiddenOffset, 0);
    }

    /// <summary>
    /// 纵向显隐：淡入淡出加一点上下位移。贴着别的窗口出现的跟随条用它，硬切太生硬。
    /// </summary>
    /// <param name="control">目标控件</param>
    /// <param name="isShowed">显示还是隐藏</param>
    /// <param name="onCompleted">播完回调（被新动画打断则不回调）</param>
    /// <param name="fromHidden">是否从隐藏态起播。截停正在播的淡出时要传 false，
    /// 否则控件会先瞬移回隐藏态再动画回来，看着像弹了一下</param>
    /// <param name="opacityChannel">透明度落到哪里。跟随窗要传原生窗口 alpha 的通道：
    /// 托管 Opacity 得等下一次渲染，赶不上原生的移动/显示（见 OverlayWindowService.TrySetNativeWindowAlpha）</param>
    public static void PlayVerticalRevealAnimation(Control? control, bool isShowed, Action? onCompleted = null,
        bool fromHidden = true, OpacityChannel? opacityChannel = null)
    {
        if (control == null)
        {
            onCompleted?.Invoke();
            return;
        }

        _ = PlayRevealCoreAsync(control, isShowed, new Vector(0, VerticalRevealHiddenOffset),
            VerticalRevealMilliseconds, resetOnShow: fromHidden, toggleHitTest: true, easeInOnHide: false,
            onCompleted, opacityChannel);
    }

    /// <summary>
    /// 把纵向显隐的目标先按到隐藏态。
    /// 要换位置再出现时，必须在挪动之前调用：否则控件会以满透明度在新位置先画出一帧，看着是闪一下。
    /// </summary>
    /// <param name="control">目标控件</param>
    public static void PrepareVerticalRevealTarget(Control? control, OpacityChannel? opacityChannel = null)
    {
        if (control == null) return;
        StopAnimation(control);
        control.IsVisible = true;
        control.IsHitTestVisible = false;
        control.RenderTransform = new TranslateTransform(0, VerticalRevealHiddenOffset);
        ApplyOpacity(control, opacityChannel, 0);
    }

    public static Task PlayNotificationTransitionAnimationAsync(Control? control, bool isShowed)
    {
        if (control == null) return Task.CompletedTask;
        return PlayNotificationTransitionAnimationCoreAsync(control, isShowed);
    }

    /// <summary>
    /// 停止动画
    /// </summary>
    /// <param name="visual"></param>
    public static void StopAnimation(Visual? visual)
    {
        if (visual == null) return;
        if (_animationCts.TryGetValue(visual, out var cts) && !cts.IsCancellationRequested)
            cts.Cancel();
    }

    private static async void PlayTransitionAnimation(IPageTransition transition, Visual? visual, bool isShowed,
        Action? onCompleted = null)
    {
        if (visual == null) return;
        if (_animationCts.TryGetValue(visual, out var cts) && !cts.IsCancellationRequested) cts.Cancel();
        cts = new CancellationTokenSource();
        _animationCts[visual] = cts;
        await transition.Start(isShowed ? null : visual, isShowed ? visual : null, isShowed,
            cts.Token);
        onCompleted?.Invoke();
        _animationCts.Remove(visual);
    }

    private static void PlayHorizontalRevealAnimation(Control control, bool isShowed, Action? onCompleted)
    {
        _ = PlayRevealCoreAsync(control, isShowed, new Vector(HorizontalRevealHiddenOffset, 0),
            ControlTransitionMilliseconds, resetOnShow: false, toggleHitTest: true, easeInOnHide: false, onCompleted);
    }

    /// <summary>
    /// 显隐动画的唯一实现：从当前值补间到目标值，位移加透明度。
    /// 同一控件上后来的动画会取消前一个（被取消就不回调 onCompleted，
    /// 隐藏播到一半又要显示时，挂在隐藏末尾的关窗才不会误触发）。
    /// </summary>
    private static async Task PlayRevealCoreAsync(Control control, bool isShowed, Vector hiddenOffset,
        int durationMs, bool resetOnShow, bool toggleHitTest, bool easeInOnHide, Action? onCompleted,
        OpacityChannel? opacityChannel = null)
    {
        if (_animationCts.TryGetValue(control, out var cts) && !cts.IsCancellationRequested) cts.Cancel();
        cts = new CancellationTokenSource();
        _animationCts[control] = cts;

        if (control.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform(isShowed ? hiddenOffset.X : 0, isShowed ? hiddenOffset.Y : 0);
            control.RenderTransform = transform;
        }

        if (toggleHitTest) control.IsHitTestVisible = isShowed;
        if (isShowed)
        {
            control.IsVisible = true;
            if (resetOnShow)
            {
                ApplyOpacity(control, opacityChannel, 0);
                transform.X = hiddenOffset.X;
                transform.Y = hiddenOffset.Y;
            }
        }

        double startOpacity = opacityChannel?.Value ?? control.Opacity;
        double startX = transform.X;
        double startY = transform.Y;
        double targetOpacity = isShowed ? 1 : 0;
        double targetX = isShowed ? 0 : hiddenOffset.X;
        double targetY = isShowed ? 0 : hiddenOffset.Y;

        try
        {
            var startTime = DateTime.UtcNow;
            while (true)
            {
                cts.Token.ThrowIfCancellationRequested();

                double elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                double progress = Math.Clamp(elapsed / durationMs, 0, 1);
                double eased = !isShowed && easeInOnHide ? progress * progress : 1 - Math.Pow(1 - progress, 3);
                // 位移要缓出才跟手，透明度跟着缓出就等于一上来就亮了（前三成时间已到 0.7），
                // 出现时看着像硬切。出现走线性，消失仍跟随位移的曲线
                double opacityEased = isShowed ? progress : eased;

                ApplyOpacity(control, opacityChannel, Lerp(startOpacity, targetOpacity, opacityEased));
                transform.X = Lerp(startX, targetX, eased);
                transform.Y = Lerp(startY, targetY, eased);

                if (progress >= 1) break;
                await Task.Delay(16, cts.Token);
            }

            ApplyOpacity(control, opacityChannel, targetOpacity);
            transform.X = targetX;
            transform.Y = targetY;
            onCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_animationCts.TryGetValue(control, out var currentCts) && currentCts == cts)
            {
                _animationCts.Remove(control);
            }
        }
    }

    private static Task PlayNotificationTransitionAnimationCoreAsync(Control control, bool isShowed)
    {
        // 通知只做轻微位移和淡入淡出，避免新增消息时影响已有卡片布局
        return PlayRevealCoreAsync(control, isShowed, new Vector(NotificationHiddenOffset, 0),
            NotificationTransitionMilliseconds, resetOnShow: true, toggleHitTest: false, easeInOnHide: true,
            onCompleted: null);
    }

    // 透明度的唯一落点：默认落到控件，调用方给了通道（如原生窗口 alpha）就走那条
    private static void ApplyOpacity(Control control, OpacityChannel? opacityChannel, double opacity)
    {
        if (opacityChannel != null) opacityChannel.Apply(opacity);
        else control.Opacity = opacity;
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + (to - from) * t;
    }

    private static CompositePageTransition CreateCompositeTransition()
    {
        var compositeTransition = new CompositePageTransition();
        var sliderTransition = new PageSlide(TimeSpan.FromMilliseconds(100), PageSlide.SlideAxis.Horizontal)
        {
            SlideInEasing = new BackEaseInOut(),
            SlideOutEasing = new BounceEaseInOut()
        };
        var fadeTransition = new CrossFade(TimeSpan.FromMilliseconds(200));
        compositeTransition.PageTransitions.Add(sliderTransition);
        compositeTransition.PageTransitions.Add(fadeTransition);
        return compositeTransition;
    }

    private static CrossFade CreateAlphaTransition()
    {
        return new CrossFade(TimeSpan.FromMilliseconds(100));
    }
}
