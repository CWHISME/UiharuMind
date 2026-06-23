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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.Core.Input;
using UiharuMind.Services;

namespace UiharuMind.Utils;

public static class WindowUtils
{
    public static void SetWindowToMousePosition(this Window window,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top, double width = 0, double height = 0, int offsetX = 0, int offsetY = 0)
    {
        var pos = App.ScreensService.MousePosition;
        var fallbackSize = GetMeasuredWindowSize(window);
        if (width <= 0 || double.IsNaN(width) || double.IsInfinity(width)) width = fallbackSize.Width;
        if (height <= 0 || double.IsNaN(height) || double.IsInfinity(height)) height = fallbackSize.Height;
        Dispatcher.UIThread.Invoke(() =>
        {
            var windowWidth = width;
            var windowHeight = height;
            var scaling = App.ScreensService.Scaling;
            double posX = pos.X;
            double posY = pos.Y;
            switch (horizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    posX -= (windowWidth - 1) * scaling;
                    break;
                case HorizontalAlignment.Center:
                    posX -= windowWidth / 2 * scaling;
                    break;
                case HorizontalAlignment.Right:
                    break;
            }

            switch (verticalAlignment)
            {
                case VerticalAlignment.Top:
                    posY -= (windowHeight - 1) * scaling;
                    break;
                case VerticalAlignment.Center:
                    posY -= windowHeight / 2 * scaling;
                    break;
                case VerticalAlignment.Bottom:
                    break;
            }

            var finalPos = new PixelPoint((int)(posX + offsetX * scaling), (int)(posY + offsetY * scaling));
            window.Position = UiUtils.EnsurePositionWithinScreen(finalPos, new Size(windowWidth, windowHeight));
        }, DispatcherPriority.MaxValue);
    }

    private static Size GetMeasuredWindowSize(Window window)
    {
        if (window.Bounds.Width > 0 && window.Bounds.Height > 0) return window.Bounds.Size;
        if (window.ClientSize.Width > 0 && window.ClientSize.Height > 0) return window.ClientSize;
        if (window.DesiredSize.Width > 0 && window.DesiredSize.Height > 0) return window.DesiredSize;

        var width = GetValidDimension(window.Width, window.MinWidth, 48);
        var height = GetValidDimension(window.Height, window.MinHeight, 48);
        return new Size(width, height);
    }

    private static double GetValidDimension(double value, double minValue, double fallback)
    {
        if (value > 0 && !double.IsNaN(value) && !double.IsInfinity(value)) return value;
        if (minValue > 0 && !double.IsNaN(minValue) && !double.IsInfinity(minValue)) return minValue;
        return fallback;
    }

    public static void SetSimpledecorationWindow(this Window window, bool isTopmost = true)
    {
        window.Topmost = isTopmost;
        window.WindowState = WindowState.Normal;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.CanResize = false;
        window.WindowDecorations = WindowDecorations.BorderOnly;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = -1;
        //不需要任务栏显示
        window.ShowInTaskbar = false;
    }


    public static void SetSimpledecorationPureWindow(this Window window, bool isTopmost = true)
    {
        //连背景边框也没有的窗口
        SetSimpledecorationWindow(window, isTopmost);
        window.WindowDecorations = WindowDecorations.None;
        window.TransparencyLevelHint = new List<WindowTransparencyLevel>()
            { WindowTransparencyLevel.Transparent, WindowTransparencyLevel.AcrylicBlur };
        //如果开启这个，会导致窗口边缘像 Border 的东西显示出来，无法做到纯透明
        window.ExtendClientAreaToDecorationsHint = false;
        window.Background = Brushes.Transparent;
        window.Foreground = Brushes.Transparent;
        window.BorderThickness = new Thickness(0);
    }

    /// <summary>
    /// 设置界面不可交互不可点击，开启时不影响前台界面
    /// </summary>
    /// <param name="window"></param>
    public static void SetNonInteractiveOverlayWindow(this Window window)
    {
        window.ShowActivated = false;
        window.Focusable = false;
        window.IsHitTestVisible = false;
        window.ShowInTaskbar = false;

        OverlayWindowService.ApplyNativeNonInteractiveStyle(window);
    }

    public static void SetScreenCenterPosition(this Window window)
    {
        // 获取当前激活的屏幕
        var screen = App.ScreensService.MouseScreen;
        if (screen != null)
        {
            // 计算窗口在屏幕中心的坐标
            var winSize = window.ClientSize;
            var x = screen.WorkingArea.Right - (screen.WorkingArea.Width + winSize.Width) / 2;
            var y = screen.WorkingArea.Bottom - (screen.WorkingArea.Height) / 2f - winSize.Height;

            // 设置窗口位置
            window.Position = new PixelPoint((int)x, (int)y);
        }
    }

    /// <summary>
    /// 鼠标是否在窗口内
    /// </summary>
    /// <param name="window"></param>
    /// <returns></returns>
    public static bool IsMouseOverWindow(this Window window)
    {
        return UiUtils.IsMouseInRange(window.Position, new Size(window.Width, window.Height));
    }

    /// <summary>
    /// 鼠标是否在窗口内？如果检测通过，则执行回调
    /// </summary>
    /// <param name="window"></param>
    /// <param name="callback"></param>
    public static void CheckMouseOverWindow(this Window window, Action callback)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (IsMouseOverWindow(window))
            {
                callback();
            }
        });
    }

    /// <summary>
    /// 鼠标是否在窗口外？如果检测通过，则执行回调
    /// </summary>
    /// <param name="window"></param>
    /// <param name="callback"></param>
    public static void CheckMouseOutsideWindow(this Window window, Action callback)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!IsMouseOverWindow(window))
            {
                callback();
            }
        });
    }

    /// <summary>
    /// 获取视觉树的根
    /// </summary>
    /// <param name="control"></param>
    /// <returns></returns>
    public static Window GetParentWindow(this Control control)
    {
        var parent = TopLevel.GetTopLevel(control);
        return (Window)parent!;
    }
}
