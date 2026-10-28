using System.IO;
using Avalonia.Platform;
using SkiaSharp;

namespace UiharuMind.Shared.Utils;

/// <summary>Skia 位图小工具：读素图、算旋转轴心。托盘帧与应用内转圈共用，两边才绕同一个点转</summary>
internal static class SkiaBitmapUtils
{
    /// <summary>
    /// 解码 Assets 下的位图
    /// </summary>
    /// <param name="fileName">Assets 下的文件名</param>
    /// <returns>位图，调用方释放</returns>
    public static SKBitmap DecodeAsset(string fileName)
    {
        using Stream stream = AssetLoader.Open(IconUtils.AssetUri(fileName));
        return SKBitmap.Decode(stream);
    }

    /// <summary>
    /// 只留 alpha、整张染成同一个颜色。素图是纯黑剪影，染色后仍是同一朵花，只是换成别的墨色
    /// </summary>
    /// <param name="source">素图</param>
    /// <param name="color">目标色</param>
    /// <returns>染色后的位图，调用方释放</returns>
    public static SKBitmap Tint(SKBitmap source, SKColor color)
    {
        SKBitmap tinted = new(source.Width, source.Height);
        using SKCanvas canvas = new(tinted);
        using SKPaint paint = new() { ColorFilter = SKColorFilter.CreateBlendMode(color, SKBlendMode.SrcIn) };
        using SKImage sourceImage = SKImage.FromBitmap(source);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawImage(sourceImage, 0, 0, SKSamplingOptions.Default, paint);
        return tinted;
    }

    /// <summary>
    /// 位图的 alpha 质心（旋转轴心）。手绘素材的重心未必在画布正中，绕质心转重心才稳
    /// </summary>
    /// <param name="bmp">位图</param>
    /// <returns>质心的像素坐标（连续坐标：第 x 列像素覆盖 [x, x+1]）；全透明时为画布中心</returns>
    public static (float X, float Y) AlphaCentroid(SKBitmap bmp)
    {
        long sumX = 0;
        long sumY = 0;
        long sumA = 0;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                byte a = bmp.GetPixel(x, y).Alpha;
                if (a == 0) continue;
                sumX += x * a;
                sumY += y * a;
                sumA += a;
            }
        }

        // 加权用的是像素下标，像素中心在下标 + 0.5；少了这半格，轴心整体偏左上，转起来重心画小圈
        return sumA == 0
            ? (bmp.Width / 2f, bmp.Height / 2f)
            : (sumX / (float)sumA + 0.5f, sumY / (float)sumA + 0.5f);
    }
}
