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
    /// <param name="offset">计算时额外增加的偏移</param>
    /// <returns></returns>
    public static PixelPoint EnsurePositionWithinScreen(Screen? screen, PixelPoint onScreenPosition,
        Size controlSize, Size offset = default)
    {
        if (screen == null) return new PixelPoint(0, 0);

        // 计算对象位置在屏幕内的有效范围
        int posX = Math.Clamp(onScreenPosition.X, screen.Bounds.Position.X,
            screen.Bounds.Position.X + screen.Bounds.Width);
        int posY = Math.Clamp(onScreenPosition.Y, screen.Bounds.Position.Y,
            screen.Bounds.Position.Y + screen.Bounds.Height);

        // 使用ClampToBounds计算控件在屏幕内的位置
        return ClampToBounds(new PixelPoint(posX, posY), controlSize, screen.Bounds, screen.Scaling);
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

        // 使用ClampToBounds计算控件在目标容器内的位置
        return ClampToBounds(contentPosition, contentSize,
            new PixelRect(targetPosition, PixelSize.FromSize(targetSize, scaling)), scaling);
    }

    /// <summary>
    /// 将坐标限制在边界内
    /// </summary>
    /// <param name="position"></param>
    /// <param name="size"></param>
    /// <param name="bounds"></param>
    /// <param name="scaling"></param>
    /// <returns></returns>
    public static PixelPoint ClampToBounds(PixelPoint position, Size size, PixelRect bounds, double scaling = 1.0)
    {
        // 计算控件在边界内的最大允许位置
        double maxX = bounds.X + bounds.Width - size.Width * scaling;
        double maxY = bounds.Y + bounds.Height - size.Height * scaling;

        // 确保坐标不会超出边界
        double x = Math.Max(bounds.X, Math.Min(position.X, maxX));
        double y = Math.Max(bounds.Y, Math.Min(position.Y, maxY));

        return new PixelPoint((int)x, (int)y);
    }

    /// <summary>
    /// 判断鼠标是否在控件范围内
    /// </summary>
    /// <param name="controlPosition"></param>
    /// <param name="controlSize"></param>
    /// <returns></returns>
    public static bool IsMouseInRange(PixelPoint controlPosition, Size controlSize)
    {
        PixelPoint mousePos = App.ScreensService.MousePosition;
        var scaling = App.ScreensService.Scaling;
        var controlMaxPos = new PixelPoint((int)(controlPosition.X + controlSize.Width * scaling),
            (int)(controlPosition.Y + controlSize.Height * scaling));
        return mousePos.X >= controlPosition.X && mousePos.X <= controlMaxPos.X &&
               mousePos.Y >= controlPosition.Y && mousePos.Y <= controlMaxPos.Y;
    }


    // /// <summary>
    // /// 以宽度为基准比例缩放图片大小，并限制最大宽度和最大高度
    // /// </summary>
    // /// <param name="size"></param>
    // /// <param name="scaleFactor"></param>
    // /// <param name="aspectRatio">原始大小的 width / height 的值</param>
    // /// <param name="limitMaxWidth">限制缩放后的最大宽度</param>
    // /// <param name="limitMaxHeight">限制缩放后的最大高度</param>
    // /// <param name="limitMinWidth">限制缩放后的最小宽度</param>
    // /// <param name="limitMinHeight">限制缩放后的最小高度</param>
    // /// <returns></returns>
    // public static Size SafeScaleByWidth(this Size size, double scaleFactor, double aspectRatio, double limitMaxWidth,
    //     double limitMaxHeight, double limitMinWidth = 0, double limitMinHeight = 0)
    // {
    //     // 计算新的宽度和高度
    //     var newWidth = Math.Clamp(size.Width * scaleFactor, limitMinWidth, limitMaxWidth);
    //     var newHeight = newWidth / aspectRatio;
    //
    //     // 如果新的高度超出上限，则重新计算宽度和高度
    //     if (newHeight > limitMaxHeight || newHeight < limitMinHeight)
    //     {
    //         newHeight = limitMaxHeight;
    //         newWidth = newHeight * aspectRatio;
    //     }
    //
    //     return new Size(newWidth, newHeight);
    // }

    /// <summary>
    /// 以高度为基准比例缩放图片大小，并限制最大宽度和最大高度
    /// </summary>
    /// <param name="size"></param>
    /// <param name="scaleFactor"></param>
    /// <param name="aspectRatio"></param>
    /// <param name="limitMaxWidth"></param>
    /// <param name="limitMaxHeight"></param>
    /// <param name="limitMinWidth"></param>
    /// <param name="limitMinHeight"></param>
    /// <returns></returns>
    public static Size ScaleByWidth(this Size size, double scaleFactor, double aspectRatio, double limitMinWidth,
        double limitMinHeight, double limitMaxWidth,
        double limitMaxHeight)
    {
        return ScaleDimension(size.Width, scaleFactor, aspectRatio, limitMinWidth, limitMinHeight, limitMaxWidth,
            limitMaxHeight);
    }

    /// <summary>
    /// 以高度为基准比例缩放图片大小，并限制最大宽度和最大高度
    /// </summary>
    /// <param name="size"></param>
    /// <param name="scaleFactor"></param>
    /// <param name="aspectRatio"></param>
    /// <param name="limitMaxWidth"></param>
    /// <param name="limitMaxHeight"></param>
    /// <param name="limitMinWidth"></param>
    /// <param name="limitMinHeight"></param>
    /// <returns></returns>
    public static Size ScaleByHeight(this Size size, double scaleFactor, double aspectRatio, double limitMinWidth,
        double limitMinHeight, double limitMaxWidth,
        double limitMaxHeight)
    {
        return ScaleDimension(size.Height, scaleFactor, aspectRatio, limitMinWidth, limitMinHeight, limitMaxWidth,
            limitMaxHeight);
    }

    /// <summary>
    /// 根据基准维度进行缩放，并限制最大和最小维度
    /// </summary>
    /// <param name="baseDimension">基准维度（宽度或高度）</param>
    /// <param name="scaleFactor">缩放因子(1+缩放系数)</param>
    /// <param name="aspectRatio">基准维度/另一个维度 的比例</param>
    /// <param name="limitMaxBase">基准维度的最大限制</param>
    /// <param name="limitMaxOther">另一个维度的最大限制</param>
    /// <param name="limitMinBase">基准维度的最小限制</param>
    /// <param name="limitMinOther">另一个维度的最小限制</param>
    /// <returns>缩放后的图片大小</returns>
    private static Size ScaleDimension(double baseDimension, double scaleFactor,
        double aspectRatio,
        double limitMinBase, double limitMinOther,
        double limitMaxBase, double limitMaxOther)
    {
        double newBase = Math.Clamp(baseDimension * scaleFactor, limitMinBase, limitMaxBase);
        double newOther = newBase / aspectRatio;

        if (newOther > limitMaxOther || newOther < limitMinOther)
        {
            newOther = Math.Clamp(newOther, limitMinOther, limitMaxOther);
            newBase = newOther * aspectRatio;
        }

        return new Size(newBase, newOther);
    }
}