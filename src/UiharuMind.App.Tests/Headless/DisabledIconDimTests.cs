using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 禁用态下的 SVG 图标要跟着置灰。图标按 <c>CurrentColor</c> 上色、不吃主题给
/// <c>:disabled</c> 换的 <c>Foreground</c>；而 <c>Svg</c> 走 <c>context.Custom</c> 自绘通道，
/// <c>Opacity</c>（自身或父级）也传不进去——只剩改颜色这一条路。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class DisabledIconDimTests
{
    private const int WindowWidth = 80;
    private const int WindowHeight = 48;
    private const int PumpAttempts = 200;
    private const int BlackEnough = 40;
    private const int DimEnough = 120;

    /// <summary>禁用后图标变淡，重新启用后回到纯黑</summary>
    [Fact]
    public void DisabledButton_DimsItsSvgIcon() => HeadlessUi.Run(() =>
    {
        ThemedSvgIcon icon = NewIcon();
        Button button = new()
        {
            Width = 30, Height = 30, Padding = new Thickness(0), Content = icon,
        };
        Window window = NewWindow(button);

        Assert.True(PumpUntil(window, darkest => darkest < BlackEnough) < BlackEnough, "启用态图标应是纯黑");

        button.IsEnabled = false;
        Assert.True(PumpUntil(window, darkest => darkest > DimEnough) > DimEnough, "禁用态图标应变淡");

        button.IsEnabled = true;
        Assert.True(PumpUntil(window, darkest => darkest < BlackEnough) < BlackEnough, "重新启用后图标应恢复纯黑");
    });

    /// <summary>图标按钮（Ursa IconButton 派生，颜色由按钮显式喂给图标）走同一条规则</summary>
    [Fact]
    public void DisabledIconButton_DimsItsSvgIcon() => HeadlessUi.Run(() =>
    {
        ThemedIconButton iconButton = new()
        {
            IconName = "undo", IconWidth = 16, IconHeight = 16, CurrentColor = Colors.Black,
        };
        Window window = NewWindow(iconButton);

        Assert.True(PumpUntil(window, darkest => darkest < BlackEnough) < BlackEnough, "启用态图标应是纯黑");

        iconButton.IsEnabled = false;
        Assert.True(PumpUntil(window, darkest => darkest > DimEnough) > DimEnough, "禁用态图标应变淡");
    });

    private static ThemedSvgIcon NewIcon() => new()
    {
        IconName = "undo", Width = 16, Height = 16, CurrentColor = Colors.Black,
    };

    private static Window NewWindow(Control content)
    {
        Window window = new()
        {
            Width = WindowWidth, Height = WindowHeight,
            Background = Brushes.White,
            Content = new Border { Padding = new Thickness(8), Child = content },
        };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // Svg 源是异步加载的（工作线程解析，再回界面线程应用），属性设上不等于画得出来。
    // 这里一边泵调度队列一边等，直到画面里的最暗像素满足条件
    private static int PumpUntil(Window window, Func<int, bool> satisfied)
    {
        int darkest = 255;
        for (int attempt = 0; attempt < PumpAttempts; attempt++)
        {
            darkest = DarkestChannel(window);
            if (satisfied(darkest)) return darkest;
            Thread.Sleep(10);
        }

        return darkest;
    }

    private static int DarkestChannel(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using Bitmap frame = window.CaptureRenderedFrame()!;
        using MemoryStream stream = new();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        using SKBitmap bitmap = SKBitmap.Decode(stream.ToArray());

        int darkest = 255;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                darkest = Math.Min(darkest, Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue)));
            }
        }

        return darkest;
    }
}
