using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Views.Capture;

namespace UiharuMind.Services;

public class ClipboardService
{
    private readonly Window _target;

    private IClipboard Clipboard => _target.Clipboard!;

    public const string ImageTypePngWin = "image/png";
    public const string ImageTypePngMac = "public.png";

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
        var dataObject = new DataObject();
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream);
        memoryStream.Position = 0;
        var bytes = memoryStream.ToArray();
        dataObject.Set(ImageTypePngWin, bytes);
        dataObject.Set(ImageTypePngMac, bytes);

        Clipboard.SetDataObjectAsync(dataObject);
    }

    public async Task<Bitmap?> GetImageFromClipboard()
    {
        try
        {
            var types = await Clipboard.GetFormatsAsync();
            foreach (var type in types)
            {
                if (type is ImageTypePngWin or ImageTypePngMac)
                {
                    var data = await Clipboard.GetDataAsync(type);
                    if (data is byte[] pngBytes)
                    {
                        using var stream = new MemoryStream(pngBytes);
                        return new Bitmap(stream);
                    }
                }
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