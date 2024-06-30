using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core;
using UiharuMind.Core.Input;
using UiharuMind.Views.Capture;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public partial class SettingPageData : PageDataBase
{
    public SettingPageData()
    {
        HotKeyManager.SetHotKey(View, new KeyGesture(Key.A, KeyModifiers.Alt | KeyModifiers.Shift));
    }

    [RelayCommand]
    private async Task Click()
    {
        await UiharuCoreManager.Instance.CaptureScreen();

        var topLevel = TopLevel.GetTopLevel(View);
        // var clipboard = await topLevel.Clipboard.GetTextAsync();
        // var formats = await topLevel.Clipboard.GetFormatsAsync();

        // var image = await topLevel.Clipboard.GetDataAsync(formats[0]);
        var data = await topLevel.Clipboard.GetDataAsync("public.png");
        if (data is byte[] pngBytes)
        {
            using (var stream = new MemoryStream(pngBytes))
            {
                var bitmap = new Bitmap(stream);
                ScreenCaptureWindow.ShowWindowAtMousePosition(bitmap);
            }
        }
        else
        {
            Console.WriteLine("No PNG image found in clipboard.");
        }
    }

    protected override Control CreateView => new SettingPage();
}