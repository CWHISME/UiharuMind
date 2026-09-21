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
using UiharuMind.Shared.Services.Native;

namespace UiharuMind.Shared.Services;

/// <summary>
/// macOS 应用菜单栏的临时接管。只服务于截图遮罩，成对调用。
/// </summary>
public static class MacAppMenuService
{
    private static IntPtr _stashedMainMenu = IntPtr.Zero;

    /// <summary>
    /// 截图期间暂时收起本应用菜单：菜单标题会拦截点击（点中标题进菜单跟踪，到不了遮罩），
    /// 空菜单栏区域则会落到遮罩上。收起后除系统 Apple 菜单外整条菜单栏都可框选。
    /// 与 <see cref="RestoreAppMenuAfterCapture"/> 配对调用，重复调用以第一次为准。
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static void SuppressAppMenuForCapture()
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (_stashedMainMenu != IntPtr.Zero) return;
        try
        {
            var app = MacNative.SharedApplication();
            if (app == IntPtr.Zero) return;
            var menu = MacNative.SendPtr(app, MacNative.Selector("mainMenu"));
            if (menu == IntPtr.Zero) return;
            MacNative.Send(menu, MacNative.Selector("retain"));
            _stashedMainMenu = menu;

            var empty = MacNative.SendPtr(MacNative.SendPtr(MacNative.GetClass("NSMenu"), MacNative.Selector("alloc")),
                MacNative.Selector("init"));
            MacNative.SendPtr(app, MacNative.Selector("setMainMenu:"), empty);
            MacNative.Send(empty, MacNative.Selector("release"));
        }
        catch
        {
            _stashedMainMenu = IntPtr.Zero;
        }
    }

    /// <summary>
    /// 恢复被 <see cref="SuppressAppMenuForCapture"/> 收起的应用菜单。没有收起过则无操作。
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static void RestoreAppMenuAfterCapture()
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (_stashedMainMenu == IntPtr.Zero) return;
        try
        {
            var app = MacNative.SharedApplication();
            if (app != IntPtr.Zero)
                MacNative.SendPtr(app, MacNative.Selector("setMainMenu:"), _stashedMainMenu);
            MacNative.Send(_stashedMainMenu, MacNative.Selector("release"));
        }
        catch
        {
            // 恢复失败不抛：菜单已空顶多影响本次运行，不把截图流程搞坏
        }
        finally
        {
            _stashedMainMenu = IntPtr.Zero;
        }
    }
}
