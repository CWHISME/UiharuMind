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

namespace UiharuMind.ViewModels.ScreenCaptures;

public static class ScreenCaptureManager
{
    public static async void CaptureScreen()
    {
        if (UiharuCoreManager.Instance.IsWindows)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { new ScreenCaptureWindow().Show(); });
            return;
        }
        await GetScreenCaptureFromClipboard();
    }

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