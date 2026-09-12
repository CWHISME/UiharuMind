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
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.Shared.Services;
using UiharuMind.Core.Input;

namespace UiharuMind.Shared.Utils;

public static class WindowUtils
{
    public static void SetWindowToMousePosition(this Window window,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top, double width = 0, double height = 0, int offsetX = 0, int offsetY = 0)
    {
        var fallbackSize = GetMeasuredWindowSize(window);
        if (width <= 0 || double.IsNaN(width) || double.IsInfinity(width)) width = fallbackSize.Width;
        if (height <= 0 || double.IsNaN(height) || double.IsInfinity(height)) height = fallbackSize.Height;

        // 纯 Wayland 下客户端无法查询全局光标位置，此时按鼠标定位只会把窗口丢到一个陈旧坐标上，
        // 不如落到当前屏中央——所有依赖本方法的弹窗因此得到统一的降级行为
        if (!App.ScreensService.IsMousePositionReliable)
        {
            SetWindowToScreenCenter(window, new Size(width, height));
            return;
        }

        var pos = App.ScreensService.MousePosition;
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

    /// <summary>
    /// 把窗口放到当前活动屏的中央，用作鼠标位置不可用时的降级落点
    /// </summary>
    /// <param name="window">目标窗口</param>
    /// <param name="size">窗口尺寸（DIP）</param>
    private static void SetWindowToScreenCenter(Window window, Size size)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var screen = App.ScreensService.GetSafeActivationScreen();
            var bounds = screen.Bounds;
            var scaling = screen.Scaling;
            var finalPos = new PixelPoint(
                bounds.X + (int)((bounds.Width - size.Width * scaling) / 2),
                bounds.Y + (int)((bounds.Height - size.Height * scaling) / 2));
            window.Position = UiUtils.EnsurePositionWithinScreen(finalPos, size);
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
        // 计算窗口在屏幕中心的坐标
        var winSize = window.ClientSize;
        var x = screen.WorkingArea.Right - (screen.WorkingArea.Width + winSize.Width) / 2;
        var y = screen.WorkingArea.Bottom - (screen.WorkingArea.Height) / 2f - winSize.Height;

        // 设置窗口位置
        window.Position = new PixelPoint((int)x, (int)y);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct CocoaRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string selectorName);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendRect(
        IntPtr receiver,
        IntPtr selector,
        CocoaRect rect,
        byte display,
        byte animate);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool NativeSetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>
    /// 把位置与内容尺寸一次提交给原生窗口，macOS 走 setFrame:display:animate:，
    /// Windows 走 SetWindowPos（位置加尺寸单次调用）。
    /// Avalonia 托管层里 Position 立即生效、Width/Height 要走布局后到，分开设逐格撕裂闪烁。
    /// 成功后调用方只需同步 Width/Height 供内容布局，Position 不必再设。
    /// </summary>
    /// <param name="window">目标窗口</param>
    /// <param name="position">窗口左上（本平台 Position 口径：macOS 是 point，Windows 是物理像素）</param>
    /// <param name="clientSize">内容尺寸（DIP）</param>
    /// <returns>成功 true；其他平台或任何失败 false，调用方走托管老路</returns>
    public static bool TrySetWindowFrame(this Window window, PixelPoint position, Size clientSize)
    {
        if (OperatingSystem.IsMacOS()) return TrySetWindowFrameMac(window, position, clientSize);
        if (OperatingSystem.IsWindows()) return TrySetWindowFrameWin(window, position, clientSize);
        return false;
    }

    private static bool TrySetWindowFrameWin(Window window, PixelPoint position, Size clientSize)
    {
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero || handle.HandleDescriptor != "HWND") return false;
            var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            double scaling = screen?.Scaling ?? 1.0;
            int width = (int)Math.Round(clientSize.Width * scaling);
            int height = (int)Math.Round(clientSize.Height * scaling);
            if (width <= 0 || height <= 0) return false;
            // 与 Avalonia Win32 后端同 flags，只是多带上尺寸一次提交
            NativeSetWindowPos(handle.Handle, IntPtr.Zero, position.X, position.Y, width, height,
                SwpNoZOrder | SwpNoActivate);
            return true;
        }
        catch
        {
            // native 调用失败就回退托管老路，不能把缩放搞坏
            return false;
        }
    }

    private static bool TrySetWindowFrameMac(Window window, PixelPoint position, Size clientSize)
    {
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero) return false;
            var primary = window.Screens.Primary;
            if (primary == null) return false;
            if (clientSize.Width <= 0 || clientSize.Height <= 0) return false;

            // chrome 取 frame 与 content 的当前差值，本窗扩展 client area，正常接近 0
            var frameSize = window.FrameSize;
            var currentClient = window.ClientSize;
            double chromeWidth = frameSize.HasValue ? Math.Max(0, frameSize.Value.Width - currentClient.Width) : 0;
            double chromeHeight = frameSize.HasValue ? Math.Max(0, frameSize.Value.Height - currentClient.Height) : 0;
            double frameHeight = clientSize.Height + chromeHeight;

            // 与 Avalonia native 的 ConvertPointY 同口径：以 primary 顶为基准翻转 Y
            double primaryTop = primary.Bounds.Y + primary.Bounds.Height;
            var rect = new CocoaRect
            {
                X = position.X,
                Y = primaryTop - position.Y - frameHeight,
                Width = clientSize.Width + chromeWidth,
                Height = frameHeight
            };
            ObjcMsgSendRect(handle.Handle, SelRegisterName("setFrame:display:animate:"), rect, 1, 0);
            return true;
        }
        catch
        {
            // native 调用失败就回退托管老路，不能把缩放搞坏
            return false;
        }
    }
}