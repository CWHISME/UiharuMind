using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Shared.Services.Native;

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
/// <para>
/// ⚠️ 真关之前必须 <see cref="RestoreWindowClass"/> 换回原类。Show 之后 AppKit 已经在窗口上登记了观察者
/// （窗口的类此时常是 KVO 动态生成的子类），换类把这份登记弄丢了，销毁时注销不掉就抛 NSRangeException
/// 中止进程（「Cannot remove an observer … _windowLayerContext … from AvnPanel」，独立复现过：换类后关窗必崩，换回再关不崩）。
/// 只隐藏不关的窗口不受影响。
/// </para>
/// </summary>
public static class MacPanelWindowService
{
    private const ulong NonactivatingPanelStyleMask = 1UL << 7;

    private static readonly ConditionalWeakTable<Window, OriginalClass> Originals = new(); //换类前的原类，销毁前换回

    private sealed record OriginalClass(IntPtr Handle);

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
            var panelClass = MacNative.GetClass("AvnPanel");
            if (panelClass == IntPtr.Zero) return false;
            var currentClass = MacNative.GetObjectClass(nsWindow);
            if (currentClass != panelClass)
            {
                if (MacNative.SetObjectClass(nsWindow, panelClass) == IntPtr.Zero) return false;
                Originals.AddOrUpdate(window, new OriginalClass(currentClass));
            }

            var styleMask = MacNative.SendULongRet(nsWindow, MacNative.Selector("styleMask"));
            MacNative.SendULong(nsWindow, MacNative.Selector("setStyleMask:"), styleMask | NonactivatingPanelStyleMask);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to convert window to macOS panel: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 把转过 panel 的窗口换回原来的类（真关、原生窗口销毁之前调；见类注释）。没转过的什么都不做
    /// </summary>
    /// <param name="window">要关的窗口</param>
    public static void RestoreWindowClass(Window window)
    {
        if (!PlatformUtils.IsMacOS || !Originals.TryGetValue(window, out OriginalClass? original)) return;
        Originals.Remove(window);

        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero) return;

            var nsWindow = handle.Handle;
            var styleMask = MacNative.SendULongRet(nsWindow, MacNative.Selector("styleMask"));
            MacNative.SendULong(nsWindow, MacNative.Selector("setStyleMask:"), styleMask & ~NonactivatingPanelStyleMask);
            MacNative.SetObjectClass(nsWindow, original.Handle);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to restore macOS window class: {e.Message}");
        }
    }

    /// <summary>
    /// 给 panel 焦点：把窗口设为 key 并排到本层最前，不激活本应用。
    /// </summary>
    /// <param name="window">已转成 panel 的浮窗</param>
    public static void FocusPanel(Window window)
    {
        if (!PlatformUtils.IsMacOS) return;

        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero) return;

            MacNative.SendPtr(handle.Handle, MacNative.Selector("makeKeyAndOrderFront:"), IntPtr.Zero);
            // makeKeyAndOrderFront: 只在本应用内部排序，应用没激活时窗口仍被别的应用压着；
            // 而转成 panel 后它又不在 [NSApp orderedWindows] 里，激活时「把最前那个窗口带上来」
            // 也扫不到它——表现就是菜单栏已经是本应用、窗口却还被遮挡。orderFrontRegardless
            // 专门用来无视激活状态把窗口排到本层最前
            MacNative.Send(handle.Handle, MacNative.Selector("orderFrontRegardless"));
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

            var runningApp = MacNative.SendPtr(MacNative.GetClass("NSRunningApplication"),
                MacNative.Selector("currentApplication"));
            if (runningApp == IntPtr.Zero) return;

            // 只带 ignoringOtherApps(1<<1)，不带 activateAllWindows(1<<0)
            MacNative.SendULong(runningApp, MacNative.Selector("activateWithOptions:"), 2);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to activate macOS app for window: {e.Message}");
        }
    }

}
