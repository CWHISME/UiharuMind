using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Shared.Services;

/// <summary>
/// 把快捷浮窗变成 macOS 的 nonactivating panel。
/// <para>
/// 普通 NSWindow 要拿键盘焦点，所属应用就必须是 active，而应用一 active，AppKit 就会把它的
/// 其它窗口（包括后台的主界面）一起带到前面；浮窗退场时同样会挑一个窗口顶上来。
/// nonactivating panel 是系统给这类工具面板准备的正解：能当 key 接收键盘，但不需要应用 active，
/// 也永远不当 main，于是前台一直是用户原来那个应用，主界面的层级自始至终没人动。
/// </para>
/// <para>
/// Avalonia 用同一份源码编出了 <c>AvnWindow</c> 与 <c>AvnPanel</c>（native 里靠 IS_NSPANEL 分支），
/// 两者 ivar 布局一致，所以能直接换类；拿不到 AvnPanel 就原样返回，调用方退回普通窗口行为。
/// </para>
/// </summary>
public static class MacPanelWindowService
{
    private const ulong NonactivatingPanelStyleMask = 1UL << 7;

    /// <summary>
    /// 把窗口转成 nonactivating panel。须在 Show 之后调用（此前拿不到原生句柄）。
    /// </summary>
    /// <param name="window">目标浮窗</param>
    /// <returns>转换成功 true；非 macOS 或拿不到 AvnPanel 时 false</returns>
    public static bool TryMakeNonactivatingPanel(Window window)
    {
        if (!PlatformUtils.IsMacOS) return false;

        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero) return false;

            var nsWindow = handle.Handle;
            var panelClass = ObjcGetClass("AvnPanel");
            if (panelClass == IntPtr.Zero) return false;
            if (ObjcGetObjectClass(nsWindow) != panelClass && ObjcSetClass(nsWindow, panelClass) == IntPtr.Zero)
                return false;

            var styleMask = ObjcMsgSendULongRet(nsWindow, SelRegisterName("styleMask"));
            ObjcMsgSendULong(nsWindow, SelRegisterName("setStyleMask:"), styleMask | NonactivatingPanelStyleMask);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to convert window to macOS panel: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 给 panel 焦点：只把窗口带到前面并设为 key，不激活本应用。
    /// </summary>
    /// <param name="window">已转成 panel 的浮窗</param>
    public static void FocusPanel(Window window)
    {
        if (!PlatformUtils.IsMacOS) return;

        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero) return;
            ObjcMsgSendPtr(handle.Handle, SelRegisterName("makeKeyAndOrderFront:"), IntPtr.Zero);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to focus macOS panel: {e.Message}");
        }
    }

    /// <summary>
    /// 激活本应用，但只把这一个窗口带到前台。
    /// <para>
    /// 普通层级的窗口在后台应用里升不上来——macOS 不让后台应用压过前台应用，所以浮窗只做
    /// nonactivating panel 还不够，非置顶的那些必须激活应用才看得见。这里用
    /// <c>NSRunningApplication.activateWithOptions:</c> 且**不带** activateAllWindows 位，
    /// 避免 Avalonia 的 activateIgnoringOtherApps: 那种「整组抬窗」把后台主界面也拽上来。
    /// </para>
    /// </summary>
    /// <param name="window">已取得 key 的浮窗</param>
    public static void ActivateAppForWindowOnly(Window window)
    {
        if (!PlatformUtils.IsMacOS) return;

        try
        {
            // 激活瞬间不让其它窗口当 key，否则 AppKit 会把 key 还给上一个 key 窗口并抬起它
            MacWindowFocusGuard.SuppressKeyHandoff(window, TimeSpan.FromMilliseconds(500));

            var runningApp = ObjcMsgSendPtrNoArg(ObjcGetClass("NSRunningApplication"),
                SelRegisterName("currentApplication"));
            if (runningApp == IntPtr.Zero) return;

            // 只带 ignoringOtherApps(1<<1)，不带 activateAllWindows(1<<0)
            ObjcMsgSendULong(runningApp, SelRegisterName("activateWithOptions:"), 2);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to activate macOS app for window: {e.Message}");
        }
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string selectorName);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass(string className);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "object_getClass")]
    private static extern IntPtr ObjcGetObjectClass(IntPtr obj);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "object_setClass")]
    private static extern IntPtr ObjcSetClass(IntPtr obj, IntPtr cls);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern ulong ObjcMsgSendULongRet(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendPtrNoArg(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendULong(IntPtr receiver, IntPtr selector, ulong value);
}
