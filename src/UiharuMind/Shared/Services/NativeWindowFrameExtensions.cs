/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using Avalonia;
using Avalonia.Controls;
using UiharuMind.Shared.Services.Native;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Shared.Services;

/// <summary>
/// 窗口几何的原子提交：位置与尺寸一次落到原生窗口。
/// </summary>
public static class NativeWindowFrameExtensions
{
    /// <summary>
    /// 把位置与内容尺寸一次提交给原生窗口，macOS 走 setFrame:display:animate:，
    /// Windows 走 SetWindowPos（位置加尺寸单次调用）。
    /// Avalonia 托管层里 Position 立即生效、Width/Height 要走布局后到，分开设逐格撕裂闪烁。
    /// 成功后调用方只需同步 Width/Height 供内容布局，Position 不必再设。
    /// </summary>
    /// <param name="window">目标窗口</param>
    /// <param name="position">窗口左上（本平台 Position 口径：macOS 是 point，Windows 是物理像素）</param>
    /// <param name="clientSize">窗口客户区尺寸（DIP，即含阴影留白的整窗尺寸，不是图片尺寸）</param>
    /// <returns>成功 true；其他平台或任何失败 false，调用方走托管老路</returns>
    public static bool TrySetWindowFrame(this Window window, PixelPoint position, Size clientSize)
    {
        return window.TrySetWindowFrame(position, clientSize, out _);
    }

    /// <summary>
    /// 同上，另带出<b>真正落盘的客户区尺寸</b>（DIP，与入参同口径）。
    /// <para>
    /// Windows 的窗口只能落在整物理像素上，所以入参会先对齐到「托管 resize 能保持」的整像素
    /// （见 <see cref="DisplayUnits.SnapToPixels"/>），落盘尺寸与入参可能差不到 1px。
    /// <b>调用方必须用 <paramref name="appliedSize"/> 回填自己的尺寸记账</b>，
    /// 不要再用自己算的那个：账目一旦和真实窗口分叉，逐格缩放就会把那点差当成残差累积成抖动。
    /// </para>
    /// </summary>
    /// <param name="window">目标窗口</param>
    /// <param name="position">窗口左上（本平台 Position 口径）</param>
    /// <param name="clientSize">期望的窗口客户区尺寸（DIP）</param>
    /// <param name="appliedSize">实际落盘的客户区尺寸（DIP），失败时原样返回入参</param>
    /// <returns>成功 true；其他平台或任何失败 false，调用方走托管老路</returns>
    public static bool TrySetWindowFrame(this Window window, PixelPoint position, Size clientSize,
        out Size appliedSize)
    {
        appliedSize = clientSize;
        if (OperatingSystem.IsMacOS()) return TrySetWindowFrameMac(window, position, clientSize);
        if (OperatingSystem.IsWindows()) return TrySetWindowFrameWin(window, position, clientSize, out appliedSize);
        return false;
    }

    private static bool TrySetWindowFrameWin(Window window, PixelPoint position, Size clientSize,
        out Size appliedSize)
    {
        appliedSize = clientSize;
        try
        {
            if (!Win32Native.TryGetHwnd(window, out var hwnd)) return false;
            // 与 Avalonia 布局取整、PixelSize.FromSize 同一个倍率（见 DisplayUnits.WindowPixelsPerDip）
            double scaling = DisplayUnits.WindowPixelsPerDip(window);
            if (scaling <= 0) return false;

            // 先对齐到「托管 resize 能保持的整物理像素」再提交：随后 Width/Height 的托管 resize
            // 会按后端同一个换算（(int)(DIP × scale) 截断）算出同一个像素尺寸，
            // 撞上 WindowImpl.Resize 的「client 尺寸已相等」分支直接返回，同一次缩放只提交一次
            Size snapped = DisplayUnits.SnapToPixels(clientSize, scaling);
            int width = (int)(snapped.Width * scaling);
            int height = (int)(snapped.Height * scaling);
            if (width <= 0 || height <= 0) return false;

            // 与 Avalonia Win32 后端同 flags，只是多带上尺寸一次提交
            Win32Native.SetWindowPos(hwnd, IntPtr.Zero, position.X, position.Y, width, height,
                Win32Native.SwpNoZOrder | Win32Native.SwpNoActivate);
            appliedSize = snapped;
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
            if (!MacNative.TryGetNsWindow(window, out var nsWindow)) return false;
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
            var rect = new MacNative.CocoaRect
            {
                X = position.X,
                Y = primaryTop - position.Y - frameHeight,
                Width = clientSize.Width + chromeWidth,
                Height = frameHeight
            };
            MacNative.SendRect(nsWindow, MacNative.Selector("setFrame:display:animate:"), rect, 1, 0);
            return true;
        }
        catch
        {
            // native 调用失败就回退托管老路，不能把缩放搞坏
            return false;
        }
    }
}
