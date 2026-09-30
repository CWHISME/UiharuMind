using System;
using System.IO;
using Avalonia.Controls;
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
    private const float WobbleAmplitude = 12f; //摆动的最大角度（度）

    /// <summary>绕<b>alpha 质心</b>旋转的帧序列（Running）。手绘花瓣不完全对称，
    /// 绕画布中心转会感觉重心在跳；绕质心转则视觉重心稳定。
    /// 旋转不改变外接圆，用原尺寸画布即可，避免大画布留白被菜单栏缩放后整张图变小</summary>
    public static WindowIcon[] BuildRotationFrames(SKBitmap source, int frameCount)
    {
        float[] angles = new float[frameCount];
        for (int i = 0; i < frameCount; i++) angles[i] = 360f * i / frameCount;
        return BuildAngleFrames(source, angles);
    }

    /// <summary>绕 alpha 质心左右摆动的帧序列（AwaitingApproval）：像摇头提醒，一个来回按正弦走。
    /// 只旋转不水平偏移——偏移会超出画布或被菜单栏缩放后占用比变小</summary>
    /// <param name="source">素图</param>
    /// <param name="frameCount">一个来回的帧数：来回的时长由调用方按帧时长换算，帧率变了摆速不变</param>
    public static WindowIcon[] BuildWobbleFrames(SKBitmap source, int frameCount)
    {
        float[] angles = new float[frameCount];
        for (int i = 0; i < frameCount; i++)
            angles[i] = WobbleAmplitude * MathF.Sin(2 * MathF.PI * i / frameCount);
        return BuildAngleFrames(source, angles);
    }

    /// <summary>按给定角度序列逐帧渲染：绕 alpha 质心旋转后编码成窗口图标</summary>
    private static WindowIcon[] BuildAngleFrames(SKBitmap source, float[] angles)
    {
        int canvas = source.Width;
        (float cx, float cy) = SkiaBitmapUtils.AlphaCentroid(source);
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
}
