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
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Utils;

public static class UiAnimationUtils
{
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
        PlayTransitionAnimation(HorizontalSlideTransition, visual, isShowed, onCompleted);
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