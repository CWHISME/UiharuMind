using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform;
using SkiaSharp;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Shared.Services;

/// <summary>
/// 托盘图标的位图渲染：把素图底图转成各状态用的窗口图标帧。
/// 与 TrayStatusIndicator（状态机 + 平台分发）分开，避免单文件堆两类职责。
/// </summary>
internal static class TrayFrameBuilder
{
    // Windows 分支的角标直径占边长比例。0.28 ≈ 18px 上直径 5px：舒适下限，再小就丢了
    private const float BadgeDiameterRatio = 0.28f;

    /// <summary>绕<b>alpha 质心</b>旋转的帧序列（Running）。手绘花瓣不完全对称，
    /// 绕画布中心转会感觉重心在跳；绕质心转则视觉重心稳定。
    /// 旋转不改变外接圆，用原尺寸画布即可，避免大画布留白被菜单栏缩放后整张图变小</summary>
    public static WindowIcon[] BuildRotationFrames(SKBitmap source, int frameCount)
    {
        float[] angles = new float[frameCount];
        for (int i = 0; i < frameCount; i++) angles[i] = 360f * i / frameCount;
        return BuildAngleFrames(source, angles);
    }

    /// <summary>绕 alpha 质心左右摆动的帧序列（AwaitingApproval）：像摇头提醒。
    /// 只旋转不水平偏移——偏移会超出画布或被菜单栏缩放后占用比变小</summary>
    public static WindowIcon[] BuildWobbleFrames(SKBitmap source)
    {
        float[] angles = { 0f, 6f, 12f, 6f, 0f, -6f, -12f, -6f };
        return BuildAngleFrames(source, angles);
    }

    /// <summary>按给定角度序列逐帧渲染：绕 alpha 质心旋转后编码成窗口图标</summary>
    private static WindowIcon[] BuildAngleFrames(SKBitmap source, float[] angles)
    {
        int canvas = source.Width;
        (float cx, float cy) = AlphaCentroid(source);
        var frames = new WindowIcon[angles.Length];
        for (int i = 0; i < angles.Length; i++)
        {
            using SKBitmap bmp = new(canvas, canvas);
            using SKCanvas c = new(bmp);
            c.Clear(SKColors.Transparent);
            c.Translate(canvas / 2f, canvas / 2f);
            c.RotateDegrees(angles[i]);
            c.DrawBitmap(source, -cx, -cy, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), null);
            frames[i] = ToWindowIcon(bmp, null);
        }
        return frames;
    }

    /// <summary>位图的 alpha 质心（旋转轴心）。手绘素材中心未必在画布正中，按质心旋转重心才稳</summary>
    private static (float X, float Y) AlphaCentroid(SKBitmap bmp)
    {
        long sumX = 0, sumY = 0, sumA = 0;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                byte a = bmp.GetPixel(x, y).Alpha;
                if (a > 0)
                {
                    sumX += x * a;
                    sumY += y * a;
                    sumA += a;
                }
            }
        }
        return sumA == 0
            ? (bmp.Width / 2f, bmp.Height / 2f)
            : (sumX / (float)sumA, sumY / (float)sumA);
    }

    /// <summary>底图加一个右下角的点。<paramref name="badge"/> 为空就是原图（Windows 分支用）</summary>
    public static WindowIcon ToWindowIcon(SKBitmap source, SKColor? badge)
    {
        using SKBitmap canvasBitmap = source.Copy();
        if (badge is { } color)
        {
            using SKCanvas canvas = new(canvasBitmap);
            float radius = Math.Min(canvasBitmap.Width, canvasBitmap.Height) * BadgeDiameterRatio / 2f;
            float center = radius + radius * 0.2f;
            using SKPaint ring = new() { Color = SKColors.White, IsAntialias = true };
            using SKPaint dot = new() { Color = color, IsAntialias = true };
            // 先画一圈白底再画点:图标本身可能是浅色的,不垫底的话角标会糊进去
            canvas.DrawCircle(canvasBitmap.Width - center, canvasBitmap.Height - center, radius * 1.25f, ring);
            canvas.DrawCircle(canvasBitmap.Width - center, canvasBitmap.Height - center, radius, dot);
        }

        using SKData encoded = canvasBitmap.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(encoded.ToArray());
        return new WindowIcon(stream);
    }

    /// <summary>解码 Assets 下的位图（素图底图）</summary>
    public static SKBitmap DecodeAsset(string fileName)
    {
        using Stream stream = AssetLoader.Open(IconUtils.AssetUri(fileName));
        return SKBitmap.Decode(stream);
    }
}
