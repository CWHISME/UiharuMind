using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 文字墨迹的中心要落在自己行盒的中心附近，这样和居中排在旁边的图标、圆点才对得齐。
/// MainFont 的字形天然偏上（见 TextOpticalOffsetConverter），全局样式按字号整体下移；
/// 换字体、改下移比例、动了全局 TextBlock 样式，都由这里量真实渲染的像素把关
/// </summary>
[Collection(HeadlessCollection.Name)]
public class TextOpticalCenterTests
{
    // 取整像素后各字号的残差实测都在半像素左右，超过这个就是又歪了
    private const double Tolerance = 0.75;

    [Theory]
    [InlineData("模型状态")]
    [InlineData("生图引擎没在运行，出图时会自动启动。")]
    [InlineData("PocketTTS · 121.9 MB")]
    public void InkCenter_SitsOnTheLineBoxCenter(string text) => HeadlessUi.Run(() =>
    {
        foreach (double size in new[] { 11.0, 12, 13, 14, 16, 20, 24 })
        {
            TextBlock block = new()
            {
                Text = text, FontSize = size, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Window window = new() { Width = 420, Height = 80, Background = Brushes.White, Content = block };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            double ink = InkCenterY(window);
            double box = block.Bounds.Top + block.Bounds.Height / 2;
            window.Close();
            Assert.True(Math.Abs(ink - box) <= Tolerance,
                $"「{text}」{size}px：墨迹中心 {ink:F2} 偏离行盒中心 {box:F2} 达 {ink - box:F2}px");
        }
    });

    // 按墨色深浅加权的竖直重心（逻辑像素）
    private static double InkCenterY(Window window)
    {
        using Bitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!;
        int width = frame.PixelSize.Width;
        int height = frame.PixelSize.Height;
        byte[] pixels = new byte[width * height * 4];
        GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(0, 0, width, height), pin.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            pin.Free();
        }

        double sum = 0;
        double weight = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int at = (y * width + x) * 4;
            double dark = (255 - (pixels[at] + pixels[at + 1] + pixels[at + 2]) / 3.0) / 255;
            if (dark <= 0.05) continue;
            sum += (y + 0.5) * dark;
            weight += dark;
        }

        return sum / weight * window.Bounds.Height / height;
    }
}
