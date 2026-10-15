using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Shared.Services.Native;

namespace UiharuMind.Shared.Services;

/// <summary>
/// macOS：点 Dock 图标时把本应用开着的窗口一起抬到前台，一个都没开才打开主界面。
/// <para>
/// 贴图、快捷浮窗这类辅助窗口取焦点时只激活自己（见 <see cref="MacPanelWindowService.ActivateAppForWindowOnly"/>），
/// 应用此时已是 active，再点 Dock 系统只发 reopen、不再整组抬窗，被别的应用压着的窗口就上不来。
/// </para>
/// </summary>
public static class MacDockReopenService
{
    /// <summary>
    /// 订阅 Dock 点击（Reopen）
    /// </summary>
    /// <param name="app">应用实例</param>
    /// <param name="getOpenWindows">当前开着、该随 Dock 点击抬起的窗口</param>
    /// <param name="launchMainWindow">一个窗口都没开时打开主界面</param>
    public static void Install(Application app, Func<IEnumerable<Window>> getOpenWindows, Action launchMainWindow)
    {
        if (!PlatformUtils.IsMacOS) return;
        if (app.TryGetFeature<IActivatableLifetime>() is not { } lifetime) return;

        lifetime.Activated += (_, e) =>
        {
            if (e.Kind != ActivationKind.Reopen) return;

            var windows = getOpenWindows().ToList();
            if (windows.Count == 0) launchMainWindow();
            else BringToFront(windows);
        };
    }

    private static void BringToFront(List<Window> windows)
    {
        try
        {
            var app = MacNative.SharedApplication();
            if (app == IntPtr.Zero) return;

            MacNative.SendBool(app, MacNative.Selector("activateIgnoringOtherApps:"), true);

            // 按 orderedWindows（从前到后）的位置由后往前抬，保住窗口间原有的前后次序。
            // nonactivating panel 不在这份列表里，排到最后抬；orderFrontRegardless 对 panel 也有效
            var ordered = GetOrderedWindowHandles(app);
            var handles = windows
                .Select(x => MacNative.TryGetNsWindow(x, out var nsWindow) ? nsWindow : IntPtr.Zero)
                .Where(x => x != IntPtr.Zero)
                .OrderByDescending(x => ordered.IndexOf(x));

            var orderFront = MacNative.Selector("orderFrontRegardless");
            foreach (var nsWindow in handles) MacNative.Send(nsWindow, orderFront);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to bring macOS windows to front: {e.Message}");
        }
    }

    private static List<IntPtr> GetOrderedWindowHandles(IntPtr app)
    {
        var result = new List<IntPtr>();
        var windows = MacNative.SendPtr(app, MacNative.Selector("orderedWindows"));
        if (windows == IntPtr.Zero) return result;

        var objectAtIndex = MacNative.Selector("objectAtIndex:");
        var count = MacNative.SendNint(windows, MacNative.Selector("count"));
        for (var i = 0; i < count; i++) result.Add(MacNative.SendPtr(windows, objectAtIndex, (ulong)i));
        return result;
    }
}
