using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Views.Capture;

namespace UiharuMind.Services;

public class ClipboardService
{
    private readonly Window _target;

    private IClipboard Clipboard => _target.Clipboard!;

    public ClipboardService(Window target)
    {
        _target = target;
    }

    public void CopyToClipboard(string text)
    {
        // _target.Dispatcher.Invoke(() => { Clipboard.SetText(text); });
        Clipboard.SetTextAsync(text);
    }

    public async Task<string?> GetFromClipboard()
    {
        return await Clipboard.GetTextAsync();
    }

    public void CopyImageToClipboard(Bitmap bitmap)
    {
        // _target.Dispatcher.Invoke(() => { Clipboard.SetImage(bitmap); });
        // Clipboard.SetDataObjectAsync(bitmap);
    }

    public async Task<Bitmap?> GetImageFromClipboard()
    {
        try
        {
            var data = await Clipboard.GetDataAsync("public.png");
            if (data is byte[] pngBytes)
            {
                using var stream = new MemoryStream(pngBytes);
                return new Bitmap(stream);
            }

            Console.WriteLine("No PNG image found in clipboard.");
        }
        catch (Exception e)
        {
            Log.Error(e);
        }

        return null;
    }
}