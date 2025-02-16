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

namespace UiharuMind.Utils;

public static class WindowUtils
{
    public static void SetWindowToMousePosition(this Window window,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top, int offsetX = 0, int offsetY = 0)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var pos = App.ScreensService.MousePosition;
            var scaling = App.ScreensService.Scaling;
            double posX = pos.X;
            double posY = pos.Y;
            switch (horizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    posX -= window.Width * scaling;
                    break;
                case HorizontalAlignment.Center:
                    posX -= window.Width / 2 * scaling;
                    break;
                case HorizontalAlignment.Right:
                    break;
            }

            switch (verticalAlignment)
            {
                case VerticalAlignment.Top:
                    posY -= window.Height * scaling;
                    break;
                case VerticalAlignment.Center:
                    posY -= window.Height / 2 * scaling;
                    break;
                case VerticalAlignment.Bottom:
                    break;
            }

            var finalPos = new PixelPoint((int)(posX + offsetX * scaling), (int)(posY + offsetY * scaling));
            window.Position = UiUtils.EnsurePositionWithinScreen(finalPos, window.ClientSize);
        });
    }

    public static void SetSimpledecorationWindow(this Window window, bool isTopmost = true)
    {
        window.Topmost = isTopmost;
        window.WindowState = WindowState.Normal;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.CanResize = false;
        window.SystemDecorations = SystemDecorations.BorderOnly;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        window.ExtendClientAreaTitleBarHeightHint = -1;
        //不需要任务栏显示
        window.ShowInTaskbar = false;
    }


    public static void SetSimpledecorationPureWindow(this Window window, bool isTopmost = true)
    {
        //连背景边框也没有的窗口
        SetSimpledecorationWindow(window, isTopmost);
        window.SystemDecorations = SystemDecorations.None;
        window.TransparencyLevelHint = new List<WindowTransparencyLevel>()
            { WindowTransparencyLevel.Transparent, WindowTransparencyLevel.AcrylicBlur };
        //如果开启这个，会导致窗口边缘像 Border 的东西显示出来，无法做到纯透明
        window.ExtendClientAreaToDecorationsHint = false;
        window.Background = Brushes.Transparent;
        window.Foreground = Brushes.Transparent;
        window.BorderThickness = new Thickness(0);
    }

    public static void SetScreenCenterPosition(this Window window)
    {
        // 获取当前激活的屏幕
        var screen = App.ScreensService.MouseScreen;
        if (screen != null)
        {
            // 计算窗口在屏幕中心的坐标
            var x = screen.WorkingArea.Right - (screen.WorkingArea.Width + window.Width) / 2;
            var y = screen.WorkingArea.Bottom - (screen.WorkingArea.Height) / 2f - window.Height;

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
        var parent = control.GetVisualRoot();
        return (Window)parent!;
    }
}