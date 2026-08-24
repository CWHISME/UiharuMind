using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using UiharuMind.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.UiharuScreenCapture;

namespace UiharuMind.Features.ScreenCapture.Frames;

/// <summary>
/// 按平台抓取一帧整屏画面。策略选择集中在这里，选区遮罩窗本身保持平台无关。
/// </summary>
public static class ScreenFrameProvider
{
    /// <summary>
    /// 抓取指定屏幕的整屏画面
    /// </summary>
    /// <param name="screen">目标屏幕</param>
    /// <param name="screenIndex">目标屏幕序号（Windows 的 DXGI 抓取按序号定位）</param>
    /// <param name="parentWindow">供系统授权对话框定位的父窗口，可为 null</param>
    /// <returns>整屏帧；抓取失败返回 null</returns>
    public static async Task<IScreenFrame?> CaptureAsync(Screen screen, int screenIndex, Window? parentWindow)
    {
        if (UiharuCoreManager.Instance.IsLinux) return await CaptureLinuxAsync(screen, parentWindow);
        return await CaptureWindowsAsync(screen, screenIndex);
    }

    private static async Task<IScreenFrame?> CaptureWindowsAsync(Screen screen, int screenIndex)
    {
        var image = await ScreenCaptureWin.CaptureAsync(screenIndex);
        if (image != null) return HpphScreenFrame.TryCreate(image, screen.Bounds.Position);

        Log.Warning("Failed to capture screen");
        return null;
    }

    private static async Task<IScreenFrame?> CaptureLinuxAsync(Screen screen, Window? parentWindow)
    {
        await using var stream =
            await new ScreenCaptureLinux().CaptureFullScreenAsync(BuildParentWindowHandle(parentWindow));
        return stream == null ? null : SkiaScreenFrame.TryCreate(stream, screen.Bounds);
    }

    /// <summary>
    /// 构造 Portal 的 parent_window 句柄。
    /// 不能传空串：xdg-desktop-portal-gnome 46 起会拒绝空句柄，授权框将无法弹出。
    /// </summary>
    /// <param name="window">父窗口，可为 null</param>
    /// <returns>Portal 句柄字符串</returns>
    public static string BuildParentWindowHandle(Window? window)
    {
        var handle = window?.TryGetPlatformHandle();
        if (handle != null && handle.HandleDescriptor == "XID" && handle.Handle != IntPtr.Zero)
        {
            return $"x11:0x{handle.Handle.ToInt64():x}";
        }

        return "wayland:";
    }
}
