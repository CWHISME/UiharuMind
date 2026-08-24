using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
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

    /// <summary>
    /// Linux 交互式截图：选框由 portal/Shell 绘制（盖住菜单栏与 dock），返回已裁剪的图片。
    /// 非 Linux 平台返回 null（该路径只服务于 GNOME Wayland 等无法用普通窗口覆盖面板的场景）。
    /// </summary>
    /// <param name="parentWindow">供 Portal 定位父窗口句柄</param>
    /// <returns>裁剪后的位图；用户取消或失败返回 null</returns>
    public static async Task<Bitmap?> CaptureInteractiveAsync(Window? parentWindow)
    {
        if (!UiharuCoreManager.Instance.IsLinux) return null;

        var handle = BuildParentWindowHandle(parentWindow);
        await using var stream = await new ScreenCaptureLinux().CaptureInteractiveAsync(handle);
        if (stream == null) return null;

        // new Avalonia.Bitmap 必须在 UI 线程构造，否则 X11 后端触碰 Xlib 崩溃
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                buffer.Position = 0;
                return new Bitmap(buffer);
            }
            catch (Exception e)
            {
                Log.Warning($"交互式截图解码失败：{e.Message}");
                return null;
            }
        });
    }

    private static async Task<IScreenFrame?> CaptureLinuxAsync(Screen screen, Window? parentWindow)
    {
        await using var stream =
            await new ScreenCaptureLinux().CaptureFullScreenAsync(BuildParentWindowHandle(parentWindow));
        if (stream == null) return null;

        // SkiaScreenFrame.TryCreate 内部会 new Avalonia.Bitmap，必须在 UI 线程构造，
        // 否则在 X11 后端下会触碰 Xlib 触发 xcb_xlib_threads_sequence_lost 崩溃
        return await Dispatcher.UIThread.InvokeAsync(() => SkiaScreenFrame.TryCreate(stream, screen.Bounds));
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
