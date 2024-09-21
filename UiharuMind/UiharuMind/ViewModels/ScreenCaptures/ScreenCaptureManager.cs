using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UiharuMind.Core;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Views.Windows.ScreenCapture;

namespace UiharuMind.ViewModels.ScreenCaptures;

public static class ScreenCaptureManager
{
    private static ScreenCaptureDockWindow? _dockWindow;

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
            Dispatcher.UIThread.InvokeAsync(() => { new ScreenCaptureWindow().Show(); });
            return;
        }

        await GetScreenCaptureFromClipboard();
    }

    public static void SyncDockWindow(ScreenCapturePreviewWindow window)
    {
        ScreenCaptureDocker.SetMainWindow(window);
    }

    // public static void SyncBreakDockWindow(Window window)
    // {
    //     DockWindow.SetMainWindow(null);
    // }

    public static async Task GetScreenCaptureFromClipboard()
    {
        await UiharuCoreManager.Instance.CaptureScreen();

        // var topLevel = TopLevel.GetTopLevel(View);
        // var clipboard = await topLevel.Clipboard.GetTextAsync();
        // var formats = await App.Current?.Clipboard?.GetFormatsAsync()!;
        // var image = await topLevel.Clipboard.GetDataAsync(formats[0]);
        ScreenCapturePreviewWindow.ShowWindowAtMousePosition(await App.Clipboard.GetImageFromClipboard());
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