/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Runtime.Versioning;
using Avalonia.Controls;
using UiharuMind.Shared.Services.Native;

namespace UiharuMind.Shared.Services;

/// <summary>
/// 窗口在窗口系统里的外观与地位：置顶档位、跨 Space、点击穿透、整体透明度。
/// 不管窗口摆在哪、多大（那是 <see cref="NativeWindowFrameExtensions"/> 的事）。
/// </summary>
public static class OverlayWindowService
{
    private const ulong NsWindowCollectionBehaviorCanJoinAllSpaces = 1;
    private const ulong NsWindowCollectionBehaviorFullScreenAuxiliary = 1u << 8;

    /// <summary>
    /// 让窗口不接收鼠标事件（点击穿透到底下的应用）。
    /// </summary>
    /// <param name="window">目标窗口</param>
    public static void ApplyNativeNonInteractiveStyle(Window window)
    {
        if (OperatingSystem.IsWindows()) ApplyWindowsClickThroughStyle(window);
        else if (OperatingSystem.IsMacOS()) ApplyMacNonInteractiveStyle(window);
    }

    /// <summary>
    /// 全屏截图标注层：macOS 下把窗口抬到菜单栏之上并允许跨 Space
    /// （Topmost 只到 floating 层，盖不住菜单栏）。Windows 下 Topmost 已够，无需处理。
    /// 在窗口 Show 之后调用，Show 之前 native 行为可能被重置。
    /// </summary>
    /// <param name="window">全屏覆盖窗口</param>
    public static void ApplyNativeFullscreenOverlayStyle(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        ApplyNativeWindowLevel(window, EOverlayWindowLevel.FullscreenOverlay);
        ApplyMacJoinAllSpaces(window);
    }

    /// <summary>
    /// 把窗口抬到指定的置顶档位（仅 macOS 有效；Windows 的 Topmost 只有一档，无需处理）。
    /// <b>所有越过菜单栏的窗口都从这里取层级</b>，谁压谁只看 <see cref="EOverlayWindowLevel"/> 的排序。
    /// 在窗口 Show 之后调用，Show 之前 native 设置可能被重置。
    /// </summary>
    /// <param name="window">目标窗口</param>
    /// <param name="level">档位</param>
    public static void ApplyNativeWindowLevel(Window window, EOverlayWindowLevel level)
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (!MacNative.TryGetNsWindow(window, out var nsWindow)) return;
        MacNative.SendLong(nsWindow, MacNative.Selector("setLevel:"), (long)level);
    }

    /// <summary>
    /// 设置原生窗口整体透明度（目前只有 macOS 有实现）。
    /// <para>
    /// 托管层的 <c>Opacity</c> 要等下一次渲染才生效，而移动/显示窗口是原生立即生效的——
    /// 两者撞在一起时，合成器会把上一帧渲染好的内容直接贴到新位置，看着就是闪一下。
    /// 原生 alpha 由 WindowServer 当场应用，不经渲染，显隐动画用它才不会漏那一帧。
    /// </para>
    /// <para>
    /// Windows 要做得用 <c>WS_EX_LAYERED</c> 加 <c>SetLayeredWindowAttributes</c>，
    /// 会改变窗口的合成方式，没在 Windows 上实测过之前不加；X11 要看合成器认不认
    /// <c>_NET_WM_WINDOW_OPACITY</c>，Wayland 没有对应协议。
    /// </para>
    /// </summary>
    /// <param name="window">目标窗口</param>
    /// <param name="alpha">0~1</param>
    /// <returns>设置成功返回 True；其他平台或取不到句柄返回 False，调用方回退到托管 Opacity</returns>
    public static bool TrySetNativeWindowAlpha(Window window, double alpha)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        if (!MacNative.TryGetNsWindow(window, out var nsWindow)) return false;
        MacNative.SendDouble(nsWindow, MacNative.Selector("setAlphaValue:"), Math.Clamp(alpha, 0, 1));
        return true;
    }

    [SupportedOSPlatform("macos")]
    private static void ApplyMacNonInteractiveStyle(Window window)
    {
        if (!MacNative.TryGetNsWindow(window, out var nsWindow)) return;
        MacNative.SendBool(nsWindow, MacNative.Selector("setIgnoresMouseEvents:"), true);
    }

    [SupportedOSPlatform("macos")]
    private static void ApplyMacJoinAllSpaces(Window window)
    {
        if (!MacNative.TryGetNsWindow(window, out var nsWindow)) return;
        MacNative.SendULong(nsWindow, MacNative.Selector("setCollectionBehavior:"),
            NsWindowCollectionBehaviorCanJoinAllSpaces | NsWindowCollectionBehaviorFullScreenAuxiliary);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsClickThroughStyle(Window window)
    {
        if (!Win32Native.TryGetHwnd(window, out var hwnd)) return;

        var exStyle = Win32Native.GetWindowLongPtr(hwnd, Win32Native.GwlExStyle);
        var newExStyle = new IntPtr(exStyle.ToInt64() | Win32Native.WsExTransparent | Win32Native.WsExNoActivate |
                                    Win32Native.WsExToolWindow);
        Win32Native.SetWindowLongPtr(hwnd, Win32Native.GwlExStyle, newExStyle);
    }
}
