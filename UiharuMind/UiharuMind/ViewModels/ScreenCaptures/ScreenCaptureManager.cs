using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UiharuMind.Core;
using UiharuMind.Core.Core.Logs;
using UiharuMind.Core.Core.Process;
using UiharuMind.Views.Capture;

namespace UiharuMind.ViewModels.ScreenCaptures;

public static class ScreenCaptureManager
{
    public static void CaptureScreen()
    {
        Dispatcher.UIThread.InvokeAsync(() => { new ScreenCaptureWindow().Show(); });
    }

    public static async void GetScreenCaptureFromClipboard()
    {
        await UiharuCoreManager.Instance.CaptureScreen();

        // var topLevel = TopLevel.GetTopLevel(View);
        // var clipboard = await topLevel.Clipboard.GetTextAsync();
        var formats = await App.Current?.Clipboard?.GetFormatsAsync()!;
        // var image = await topLevel.Clipboard.GetDataAsync(formats[0]);
        var data = await App.Current?.Clipboard?.GetDataAsync("public.png")!;
        if (data is byte[] pngBytes)
        {
            using (var stream = new MemoryStream(pngBytes))
            {
                var bitmap = new Bitmap(stream);
                ScreenCapturePreviewWindow.ShowWindowAtMousePosition(bitmap);
            }
        }
        else
        {
            Console.WriteLine("No PNG image found in clipboard.");
        }
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