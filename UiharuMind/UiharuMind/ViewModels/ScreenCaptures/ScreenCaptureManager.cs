using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UiharuMind.Core;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Views.Capture;
using UiharuMind.Views.ScreenCapture;

namespace UiharuMind.ViewModels.ScreenCaptures;

public static class ScreenCaptureManager
{
    private static ScreenCaptureDockWindow? _dockWindow;

    private static ScreenCaptureDockWindow DockWindow
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

    public static void SyncDockWindow(Window window)
    {
        DockWindow.SetMainWindow(window);
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

    const string OcrScriptMac = @"
            tell application ""Preview""
                activate
                set theFilePath to ""{0}""
                set theDocument to open theFilePath
                set read only of theDocument to true
            end tell
        ";

    public static async void OpenOcr(string filePath)
    {
        if (UiharuCoreManager.Instance.IsMacOS)
        {
            await ProcessHelper.StartProcess("osascript", $"-e '{string.Format(OcrScriptMac, filePath)}'", null);
        }
        else Log.Error("OpenOCR is only available on macOS.");
    }
}