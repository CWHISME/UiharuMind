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
                ShowWindowAtMousePosition(bitmap);
            }
        }
        else
        {
            Console.WriteLine("No PNG image found in clipboard.");
        }
    }

    public static void ShowWindowAtMousePosition(Bitmap image)
    {
        var window = new Window
        {
            Width = image.Size.Width,
            Height = image.Size.Height,
            WindowState = WindowState.Normal,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Title = "Image Viewer",
            CanResize = false,
            SystemDecorations = SystemDecorations.BorderOnly, // 移除标题栏
            Content = new Image { Source = image },
            Topmost = true
        };

        // 当窗口打开时，设置窗口位置
        window.Opened += (sender, e) =>
        {
            var window = ((Window)sender);
            var pos = new PixelPoint(InputManager.MouseData.X, InputManager.MouseData.Y);
            // var pointerPosition = window.PointToScreen(new Point(0, 0));
            // // 将窗口的右下角定位在鼠标位置
            // window.Position = new PixelPoint((int)(pointerPosition.X - window.Width),
            //     (int)(pointerPosition.Y - window.Height));
            // window.Position = pos;
            window.Position = new PixelPoint((int)(pos.X - window.Width),
                (int)(pos.Y - window.Height));
        };

        window.PointerMoved += (sender, e) =>
        {
            if (e.KeyModifiers == KeyModifiers.None && e.Pointer.)
            {
                window.BeginMoveDrag();
            }
        };
        
        // 双击事件处理
        window.PointerPressed += (sender, e) =>
        {
            if (e.ClickCount == 2)
            {
                window.Close();
            }
        };
        // window.Position = new PixelPoint(e.Position.X - window.Width, e.Position.Y - window.Height);

        // 显示窗口
        window.Show();
    }

    protected override Control CreateView => new SettingPage();
}