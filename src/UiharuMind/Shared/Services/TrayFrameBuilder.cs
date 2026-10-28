using System;
using System.IO;
using Avalonia.Controls;
using SkiaSharp;
using UiharuMind.Shared.Spinner;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Shared.Services;

/// <summary>
/// 托盘图标的位图渲染：把素图底图转成各状态用的窗口图标帧。
/// 与 TrayStatusIndicator（状态机 + 平台差异）分开，避免单文件堆两类职责。
/// </summary>
internal static class TrayFrameBuilder
{
    private const float WobbleAmplitude = 12f; //摆动的最大角度（度）

    /// <summary>摆动一个来回的帧数（800ms ÷ <see cref="SpinClock.IntervalMs"/> 一帧）：来回的时长由帧时长换算，
    /// 帧率变了摆速不变</summary>
    public const int WobbleFrameCount = 20;

    /// <summary>一套状态帧：Idle 素图静止、Running 绕一圈、AwaitingApproval 摆一个来回</summary>
    public sealed record Frames(WindowIcon Idle, WindowIcon[] Running, WindowIcon[] Alert);

    /// <summary>把一张素图渲成三态帧</summary>
    /// <param name="source">素图（纯黑剪影 + alpha；Windows 上调用方先按任务栏明暗染好墨色）</param>
    public static Frames Build(SKBitmap source)
    {
        return new Frames(
            ToWindowIcon(source),
            BuildRotationFrames(source, SpinClock.FrameCount),
            BuildWobbleFrames(source, WobbleFrameCount));
    }

    /// <summary>绕<b>alpha 质心</b>旋转的帧序列（Running）。手绘花瓣不完全对称，
    /// 绕画布中心转会感觉重心在跳；绕质心转则视觉重心稳定。
    /// 旋转不改变外接圆，用原尺寸画布即可，避免大画布留白被菜单栏缩放后整张图变小</summary>
    private static WindowIcon[] BuildRotationFrames(SKBitmap source, int frameCount)
    {
        float[] angles = new float[frameCount];
        for (int i = 0; i < frameCount; i++) angles[i] = 360f * i / frameCount;
        return BuildAngleFrames(source, angles);
    }

    /// <summary>绕 alpha 质心左右摆动的帧序列（AwaitingApproval）：像摇头提醒，一个来回按正弦走。
    /// 只旋转不水平偏移——偏移会超出画布或被菜单栏缩放后占用比变小</summary>
    private static WindowIcon[] BuildWobbleFrames(SKBitmap source, int frameCount)
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
        using SKImage sourceImage = SKImage.FromBitmap(source);
        var frames = new WindowIcon[angles.Length];
        for (int i = 0; i < angles.Length; i++)
        {
            using SKBitmap bmp = new(canvas, canvas);
            using SKCanvas c = new(bmp);
            c.Clear(SKColors.Transparent);
            c.Translate(canvas / 2f, canvas / 2f);
            c.RotateDegrees(angles[i]);
            c.DrawImage(sourceImage, -cx, -cy, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            frames[i] = ToWindowIcon(bmp);
        }

        return frames;
    }

    /// <summary>位图编码成窗口图标</summary>
    private static WindowIcon ToWindowIcon(SKBitmap source)
    {
        using SKData encoded = source.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(encoded.ToArray());
        return new WindowIcon(stream);
    }
}
