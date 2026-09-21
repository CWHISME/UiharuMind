using System;
using System.Collections.Generic;
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
        if (UiharuCoreManager.Instance.IsMacOs) return await CaptureMacAsync(screen);
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
    /// macOS 整屏抓帧：screencapture 按屏序号静默抓取（无系统 UI）。
    /// -D 序号与 Screens.All 顺序不保证一致（3 屏以上尤其），所以按猜测顺序逐个抓、
    /// 以 PNG 尺寸匹配目标屏（point 的整数倍）为准，第一个对上的就是要抓的那块屏。
    /// 必须在遮罩窗显示之前调用，抓到的 PNG 里才没有遮罩自己。
    /// </summary>
    /// <param name="screen">目标屏幕</param>
    /// <returns>整屏帧；非 macOS 或都对不上返回 null</returns>
    public static async Task<IScreenFrame?> CaptureMacAsync(Screen screen)
    {
        if (!UiharuCoreManager.Instance.IsMacOs) return null;

        var screens = App.DummyWindow.Screens.All;
        int guessed = 0;
        for (int i = 0; i < screens.Count; i++)
        {
            if (ReferenceEquals(screens[i], screen))
            {
                guessed = i + 1;
                break;
            }
        }

        if (guessed <= 0) guessed = screen.IsPrimary ? 1 : 2;

        // 猜中的先试，其余按序号补试，PNG 尺寸对上才算抓到
        var order = new List<int> { guessed };
        for (int i = 1; i <= screens.Count; i++)
        {
            if (i != guessed) order.Add(i);
        }

        foreach (int index in order)
        {
            var frame = await TryCaptureMacDisplayAsync(index, screen);
            if (frame != null)
            {
                if (index != guessed)
                    Log.Debug($"mac 抓屏序号与屏幕顺序不一致：目标屏用 -D{index} 才对上。");
                return frame;
            }
        }

        Log.Warning("mac 整屏抓取失败：所有显示器序号都对不上目标屏。");
        return null;
    }

    private static async Task<IScreenFrame?> TryCaptureMacDisplayAsync(int displayIndex, Screen screen)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"uiharu-cap-{Guid.NewGuid():N}.png");
        try
        {
            if (!await ScreenCaptureMac.CaptureDisplayToFile(displayIndex, tmp)) return null;

            // X11 教训：位图放 UI 线程构造，各后端统一；
            // 尺寸对不上（抓错屏）TryCreate 内部会拦下返回 null
            return await Dispatcher.UIThread.InvokeAsync(() => MacScreenFrame.TryCreate(tmp, screen.Bounds));
        }
        finally
        {
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch (Exception e)
            {
                Log.Warning($"删除截图临时文件失败：{e.Message}");
            }
        }
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
