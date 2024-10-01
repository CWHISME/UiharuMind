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
    /// 计算指定大小的控件在屏幕内鼠标处的位置，防止其超出屏幕范围
    /// </summary>
    /// <param name="controlSize"></param>
    /// <returns></returns>
    public static PixelPoint EnsurePositionWithinScreen(Size controlSize)
    {
        return EnsurePositionWithinScreen(App.ScreensService.MouseScreen, App.ScreensService.MousePosition,
            controlSize);
    }

    /// <summary>
    /// 计算在屏幕内的位置，防止其超出屏幕范围
    /// </summary>
    /// <param name="onScreenPosition"></param>
    /// <param name="controlSize"></param>
    /// <returns></returns>
    public static PixelPoint EnsurePositionWithinScreen(PixelPoint onScreenPosition, Size controlSize)
    {
        return EnsurePositionWithinScreen(App.ScreensService.MouseScreen, onScreenPosition, controlSize);
    }

    /// <summary>
    /// 计算控件在屏幕内的位置，防止其超出屏幕范围
    /// </summary>
    /// <param name="screen"></param>
    /// <param name="onScreenPosition"></param>
    /// <param name="controlSize"></param>
    /// <returns></returns>
    public static PixelPoint EnsurePositionWithinScreen(Screen? screen, PixelPoint onScreenPosition,
        Size controlSize)
    {
        if (screen == null) return new PixelPoint(0, 0);
        // PixelPoint mousePixelPoint = App.ScreensService.MousePosition;
        //PixelPoint.FromPoint(, App.ScreensService.Scaling);
        // 计算对象位置在屏幕内的有效范围，确保位置初始值一定在屏幕范围内
        int posX = Math.Clamp(onScreenPosition.X, screen.Bounds.Position.X,
            screen.Bounds.Position.X + screen.Bounds.Width);
        int posY = Math.Clamp(onScreenPosition.Y, screen.Bounds.Position.Y,
            screen.Bounds.Position.Y + screen.Bounds.Height);

        // 计算控件在屏幕内的位置
        double x = Math.Max(0,
            Math.Min(posX,
                screen.Bounds.Width - controlSize.Width * screen.Scaling));
        double y = Math.Max(0,
            Math.Min(posY + 20,
                screen.Bounds.Height - controlSize.Height * screen.Scaling));

        return new PixelPoint((int)x, (int)y);
    }

    /// <summary>
    /// 计算鼠标位置在控件内的位置,并返回相对于目标校正后(使其不越界)的位置需要的偏移
    /// </summary>
    /// <param name="contentPosition"></param>
    /// <param name="contentSize"></param>
    /// <returns></returns>
    public static PixelPoint EnsureMousePositionWithinTargetOffset(PixelPoint contentPosition, Size contentSize)
    {
        var mousePos = App.ScreensService.MousePosition;
        var correctionPos = EnsurePositionWithinTarget(contentPosition, contentSize, mousePos,
            new Size(1, 1));
        return mousePos - correctionPos;
    }

    /// <summary>
    /// 计算鼠标位置在控件内的位置,并返回相对于目标校正后(使其不越界)的位置
    /// </summary>
    /// <param name="contentPosition"></param>
    /// <param name="contentSize"></param>
    /// <returns></returns>
    public static PixelPoint EnsureMousePositionWithinTarget(PixelPoint contentPosition, Size contentSize)
    {
        return EnsurePositionWithinTarget(contentPosition, contentSize, App.ScreensService.MousePosition,
            new Size(1, 1));
    }

    /// <summary>
    /// 计算指定对象在容器内的位置，并返回对象相对于容器校正后(使其不越界)的位置
    /// </summary>
    /// <param name="contentPosition">容器坐标</param>
    /// <param name="contentSize"></param>
    /// <param name="targetPosition">目标坐标</param>
    /// <param name="targetSize"></param>
    /// <returns>屏幕坐标</returns>
    public static PixelPoint EnsurePositionWithinTarget(PixelPoint contentPosition, Size contentSize,
        PixelPoint targetPosition, Size targetSize)
    {
        double scaling = App.ScreensService.Scaling;

        // 计算控件和目标容器的最大边界
        double contentMaxX = contentPosition.X + contentSize.Width * scaling;
        double contentMaxY = contentPosition.Y + contentSize.Height * scaling;
        // double targetMaxX = targetPosition.X + targetSize.Width * scaling;
        // double targetMaxY = targetPosition.Y + targetSize.Height * scaling;

        // 确保坐标不会比容器小
        double posX = Math.Max(contentPosition.X, targetPosition.X);
        double posY = Math.Max(contentPosition.Y, targetPosition.Y);

        // 确保坐标不会比容器大
        posX = Math.Min(posX, contentMaxX - targetSize.Width * scaling);
        posY = Math.Min(posY, contentMaxY - targetSize.Height * scaling);

        // // 计算控件在容器内的位置
        // double x = Math.Max(contentPosition.X, Math.Min(targetPosition.X, maxX));
        // double y = Math.Max(contentPosition.Y, Math.Min(targetPosition.Y, maxY));

        // 返回最终位置
        return new PixelPoint((int)posX, (int)posY);
    }


    //     int posX = Math.Clamp(targetPosition.X, contentPosition.X,
    //         contentPosition.X + (int)(contentSize.Width * scaling));
    //     int posY = Math.Clamp(targetPosition.Y, contentPosition.Y,
    //         contentPosition.Y + (int)(contentSize.Height * scaling));
    //
    //     // 计算控件在屏幕内的位置
    //     double x = Math.Max(0, Math.Min(posX, screen.Bounds.Width - controlSize.Width * screen.Scaling));
    //     double y = Math.Max(0, Math.Min(posY + 20, screen.Bounds.Height - controlSize.Height * screen.Scaling));
    //
    //     return new PixelPoint((int)x, (int)y);
    // }

    public static bool IsMouseInRange(PixelPoint controlPosition, Size controlSize)
    {
        PixelPoint mousePos = App.ScreensService.MousePosition;
        var scaling = App.ScreensService.Scaling;
        var controlMaxPos = new PixelPoint((int)(controlPosition.X + controlSize.Width * scaling),
            (int)(controlPosition.Y + controlSize.Height * scaling));
        return mousePos.X >= controlPosition.X && mousePos.X <= controlMaxPos.X &&
               mousePos.Y >= controlPosition.Y && mousePos.Y <= controlMaxPos.Y;
    }
}