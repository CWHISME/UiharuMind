using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace UiharuMind.Shared.Services;

/// <summary>
/// macOS 关窗焦点守卫：关掉浮窗时，不让后台的主界面被 AppKit 顺手抬到最前。
/// <para>
/// AppKit 在 key 窗口易主时会自动改派，Avalonia 又在 windowDidBecomeKey 里对新 key 窗口
/// 调 orderFront（AvnWindow.mm），两者叠加就是「关掉快捷面板，后台主界面自己冒出来」。
/// 这里在改派发生的那一小段时间里，把其它窗口的 canBecomeKeyWindow 关掉，让 AppKit 选不出候选。
/// </para>
/// <para>
/// 注意别顺手去动 canBecomeMainWindow：浮窗一旦不能当 main，AppKit 会转而提拔主界面当 main
/// 并把它抬起来，比原来还糟。浮窗正常当 main 才是对的。
/// </para>
/// </summary>
public static class MacWindowFocusGuard
{
    private static readonly List<IntPtr> SuppressedWindows = new();
    private static bool _restoreScheduled;

    /// <summary>
    /// 在关闭或隐藏窗口前调用：让本应用其它窗口暂时不能成为 key，AppKit 就选不出改派对象。
    /// 仅 macOS 生效，恢复由内部自动排期，调用方无需配对。
    /// </summary>
    /// <param name="closingWindow">正在关闭、隐藏或正在取得焦点的窗口，自身不参与抑制</param>
    /// <param name="restoreDelay">
    /// 恢复时机。null 表示下一轮派发（关窗够用）；给时长表示延迟恢复——激活场景里 AppKit 的
    /// 改派晚于当前事件循环，恢复太早等于没抑制
    /// </param>
    public static void SuppressKeyHandoff(Window closingWindow, TimeSpan? restoreDelay = null)
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        foreach (var window in desktop.Windows)
        {
            if (ReferenceEquals(window, closingWindow) || !window.IsVisible) continue;

            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero) continue;
            if (SuppressedWindows.Contains(handle.Handle)) continue;

            SetCanBecomeKeyWindow(handle.Handle, false);
            SuppressedWindows.Add(handle.Handle);
        }

        if (SuppressedWindows.Count == 0 || _restoreScheduled) return;

        _restoreScheduled = true;
        if (restoreDelay.HasValue)
        {
            DispatcherTimer.RunOnce(RestoreKeyHandoff, restoreDelay.Value);
            return;
        }

        // 关窗与缓存窗的 Hide 都跑在更高优先级，Background 能保证恢复排在改派之后
        Dispatcher.UIThread.Post(RestoreKeyHandoff, DispatcherPriority.Background);
    }

    private static void RestoreKeyHandoff()
    {
        _restoreScheduled = false;
        foreach (var nsWindow in SuppressedWindows) SetCanBecomeKeyWindow(nsWindow, true);
        SuppressedWindows.Clear();
    }

    private static void SetCanBecomeKeyWindow(IntPtr nsWindow, bool value)
    {
        try
        {
            // setCanBecomeKeyWindow: 由 Avalonia 的 AvnWindow 提供，原生 NSWindow 上没有这个 setter
            var selector = SelRegisterName("setCanBecomeKeyWindow:");
            if (!RespondsToSelector(nsWindow, SelRegisterName("respondsToSelector:"), selector)) return;
            ObjcMsgSendBool(nsWindow, selector, value);
        }
        catch
        {
            // 抑制失败顶多是老的弹窗行为，不能把关窗流程搞挂
        }
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string selectorName);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool RespondsToSelector(IntPtr receiver, IntPtr selector, IntPtr argSelector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendBool(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool value);
}
