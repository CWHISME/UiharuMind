/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UiharuMind.Features.ScreenCapture.Frames;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.UiharuScreenCapture;
using UiharuMind.Core;

namespace UiharuMind.Features.ScreenCapture;

public static class ScreenCaptureManager
{
    private static ScreenCaptureDockWindow? _dockWindow;
    private static ScreenCaptureWindow? _activeCaptureWindow;

    // 截屏会话守卫：CaptureScreen 是 async void，多次触发会跑出并发管线，
    // 叠出多个遮罩互相顶。一次会话只留一个，新的直接丢弃（Esc 关掉再截）
    private static int _captureSessionActive;

    private static ScreenCaptureDockWindow ScreenCaptureDocker
    {
        get
        {
            if (_dockWindow == null)
            {
                _dockWindow = new ScreenCaptureDockWindow();
                // Dispatcher.UIThread.InvokeAsync(() => { DockWindow.Show(); });
            }

            return _dockWindow;
        }
    }

    public static async void CaptureScreen()
    {
        if (UiharuCoreManager.Instance.IsWindows)
        {
            UIManager.ShowWindow<ScreenCaptureWindow>();
            return;
        }

        if (UiharuCoreManager.Instance.IsLinux)
        {
            await ShowLinuxCaptureOverlay();
            return;
        }

        await ShowMacCaptureOverlay();
    }

    public static void SyncDockWindow(ScreenCapturePreviewWindow? window)
    {
        ScreenCaptureDocker.SetMainWindow(window);
    }

    // public static void SyncBreakDockWindow(Window window)
    // {
    //     DockWindow.SetMainWindow(null);
    // }

    /// <summary>
    /// Mac专用，调用系统截屏至剪贴板再获取截图显示
    /// </summary>
    public static async Task GetMacScreenCaptureFromClipboard()
    {
        App.Clipboard.IsSelfCopying = true;

        var isCaptured = await ScreenCaptureMac.Capture();

        if (!isCaptured)
        {
            UIManager.ShowWindow<PermissionGuideWindow>();
            return;
        }

        var image = await App.Clipboard.GetImageFromClipboard();
        App.Clipboard.RecordImageToHistory(image);
        UIManager.ShowPreviewImageWindowAtMousePosition(image, App.ScreensService.MousePressedPosition, App.ScreensService.MouseReleasedPosition);
    }

    /// <summary>
    /// macOS：先静默抓整屏（无系统 UI），再进自家选区遮罩窗，预抓帧经 SetPreCapturedFrame 交入。
    /// 预抓失败（无权限、抓错屏）回退系统截图老路。
    /// </summary>
    private static async Task ShowMacCaptureOverlay()
    {
        if (Interlocked.Exchange(ref _captureSessionActive, 1) == 1)
        {
            Log.Debug("截图会话进行中，重复触发已丢弃。");
            return;
        }

        try
        {
            var screen = App.ScreensService.MouseScreen;
            var frame = await ScreenFrameProvider.CaptureAsync(screen, App.ScreensService.MouseScreenIndex, App.DummyWindow);
            if (frame == null)
            {
                // 回退老路不经过遮罩，会话在这里结束，不能等 OnPreClose
                try
                {
                    await GetMacScreenCaptureFromClipboard();
                }
                finally
                {
                    Interlocked.Exchange(ref _captureSessionActive, 0);
                }

                return;
            }

            // 一次只留一个遮罩：旧的不关，新旧叠在一起极易误判（层级/冻结帧都不同）
            try
            {
                _activeCaptureWindow?.Close();
            }
            catch (Exception e)
            {
                Log.Warning($"关闭旧截图遮罩失败：{e.Message}");
            }

            UIManager.ShowWindow<ScreenCaptureWindow>(window =>
            {
                _activeCaptureWindow = window;
                window.OnPreCloseEvent += () =>
                {
                    if (ReferenceEquals(_activeCaptureWindow, window)) _activeCaptureWindow = null;
                    Interlocked.Exchange(ref _captureSessionActive, 0);
                };
                window.SetPreCapturedFrame(frame, screen);
            }, isMulti: true);
        }
        catch (Exception e)
        {
            Interlocked.Exchange(ref _captureSessionActive, 0);
            Log.Warning($"打开截图遮罩失败：{e.Message}");
        }
    }

    /// <summary>
    /// Linux 专用：走 Portal 交互式截图。选框由 portal/Shell 以特权层级绘制，天然盖住菜单栏与 dock，
    /// 返回的图片已是裁剪后的区域。这样绕开了"普通应用窗口无法在 GNOME Wayland 上稳定压过面板"的限制——
    /// 自绘全屏遮罩窗会被 compositor 反复收回。选区过程中的实时放大镜等交互会交给 portal 的选框，
    /// 截图后的预览、OCR、标注仍在 UiharuMind 内完成。
    /// </summary>
    private static async Task ShowLinuxCaptureOverlay()
    {
        var bitmap = await ScreenFrameProvider.CaptureInteractiveAsync(App.DummyWindow);
        if (bitmap == null) return;

        App.Clipboard.RecordImageToHistory(bitmap);
        UIManager.ShowPreviewImageWindowAtMousePosition(
            bitmap, size: null, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    public static async void OpenOcr(string filePath, int width, int height)
    {
        if (UiharuCoreManager.Instance.IsMacOs)
        {
            var mousePos = App.ScreensService.MousePosition;
            // await ProcessHelper.StartProcess("open", $"-a Preview {filePath}", null);
            string appleScript = $@"
            set theFilePath to POSIX file ""{filePath}""

            tell application ""System Events""
                set previewRunning to (count of (every process whose name is ""Preview"")) > 0
            end tell

            if previewRunning then
                tell application ""Preview""
                    close (every window)
                end tell
            end if

            tell application ""Preview""
                activate
                open theFilePath
            end tell

            tell application ""System Events""
                repeat until (exists window 1 of process ""Preview"")
                    delay 0.01 -- 等待窗口存在
                end repeat
            end tell

            tell application ""Preview""
                set bounds of window 1 to {{{mousePos.X}, {mousePos.Y + 10}, {mousePos.X + width}, {mousePos.Y + height}}}
                -- set visible of window 1 to true
            end tell";
            await ProcessHelper.StartProcess("osascript", $"-e \"{appleScript.Replace("\"", "\\\"")}\"");
        }
        else Log.Error("OpenOCR is only available on macOS.");
    }
}