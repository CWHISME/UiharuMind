using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HPPH;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.UiharuScreenCapture;

namespace UiharuMind.Utils;

public static class UiUtils
{
    /// <summary>
    /// 将 IImage 转换为 Bitmap
    /// </summary>
    /// <param name="image"></param>
    /// <param name="dpi"></param>
    /// <returns></returns>
    public static async Task<Bitmap?> ImageToBitmap(this IImage image)
    {
        Bitmap? bitmap = null;
        await image.GetCaptureImagePointerAsync((data, stride) =>
        {
            try
            {
                bitmap = new Bitmap(
                    PixelFormat.Bgra8888,
                    AlphaFormat.Unpremul,
                    data,
                    new PixelSize(image.Width, image.Height),
                    new Vector(96, 96),
                    stride);
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
            }
        }).ConfigureAwait(false);
        return bitmap;
    }

    /// <summary>
    /// 计算控件在屏幕内的位置，防止其超出屏幕范围
    /// </summary>
    /// <param name="controlSize"></param>
    /// <returns></returns>
    public static Point CalculatePositionWithinScreen(Size controlSize)
    {
        return CalculatePositionWithinScreen(App.ScreensService.MouseScreen, controlSize);
    }

    /// <summary>
    /// 计算控件在屏幕内的位置，防止其超出屏幕范围
    /// </summary>
    /// <param name="screen"></param>
    /// <param name="controlSize"></param>
    /// <returns></returns>
    public static Point CalculatePositionWithinScreen(Screen? screen, Size controlSize)
    {
        if (screen == null) return new Point(0, 0);
        // 计算鼠标位置在屏幕内的有效范围
        PixelPoint mousePixelPoint = App.ScreensService.MousePosition;
        //PixelPoint.FromPoint(, App.ScreensService.Scaling);
        int posX = Math.Clamp(mousePixelPoint.X, screen.Bounds.Position.X,
            screen.Bounds.Position.X + screen.Bounds.Width);
        int posY = Math.Clamp(mousePixelPoint.Y, screen.Bounds.Position.Y,
            screen.Bounds.Position.Y + screen.Bounds.Height);

        // 计算控件在屏幕内的位置
        double x = Math.Max(0,
            Math.Min(posX / screen.Scaling,
                screen.Bounds.Width / screen.Scaling - controlSize.Width));
        double y = Math.Max(0,
            Math.Min(posY / screen.Scaling + 20,
                screen.Bounds.Height / screen.Scaling - controlSize.Height));

        return new Point(x, y);
    }
}