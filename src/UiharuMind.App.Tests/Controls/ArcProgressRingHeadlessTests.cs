/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Shared.Controls;
using Xunit;

namespace UiharuMind.App.Tests.Controls;

/// <summary>
/// 环是真的画出来的，不是「属性设上了、屏幕上什么都没有」。
///
/// 无头宿主开了真 Skia（见 <c>HeadlessTestApp</c>），所以这里能拿到像素。
/// 断言故意松：只问「有没有红色像素、有没有灰色像素」，不钉具体坐标——
/// 抗锯齿、线宽与半径的换算改一点点都不该让这条测试红。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ArcProgressRingHeadlessTests
{
    private const int Size = 28;

    [Fact]
    public void Render_PaintsArcAndTrack()
    {
        HeadlessUi.Run(() =>
        {
            ArcProgressRing ring = new()
            {
                Width = Size,
                Height = Size,
                Percent = 50,
                SecondaryPercent = 50,
                ArcBrush = Brushes.Red,
                TrackBrush = Brushes.LightGray,
                Thickness = 3,
            };
            ring.Measure(new Size(Size, Size));
            ring.Arrange(new Rect(0, 0, Size, Size));

            byte[] pixels = Render(ring);
            (int red, int gray) = CountColors(pixels);

            Assert.True(red > 20, $"半圈的弧一个红像素都没有（{red}）——弧没画出来");
            Assert.True(gray > 20, $"剩下那半圈没有轨道（{gray}）——占用低时会看着像个孤零零的色点");
        });
    }

    /// <summary>0% 只画轨道：整圈都在，一个红像素都没有</summary>
    [Fact]
    public void Render_ZeroPercentDrawsTrackOnly()
    {
        HeadlessUi.Run(() =>
        {
            ArcProgressRing ring = new()
            {
                Width = Size,
                Height = Size,
                Percent = 0,
                SecondaryPercent = 0,
                ArcBrush = Brushes.Red,
                TrackBrush = Brushes.LightGray,
                Thickness = 3,
            };
            ring.Measure(new Size(Size, Size));
            ring.Arrange(new Rect(0, 0, Size, Size));

            (int red, int gray) = CountColors(Render(ring));

            Assert.Equal(0, red);
            Assert.True(gray > 20);
        });
    }

    private static byte[] Render(ArcProgressRing ring)
    {
        using RenderTargetBitmap bitmap = new(new PixelSize(Size, Size), new Vector(96, 96));
        bitmap.Render(ring);
        using MemoryStream stream = new();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;

        using Bitmap decoded = new(stream);
        byte[] pixels = new byte[Size * Size * 4];
        IntPtr buffer = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            decoded.CopyPixels(new PixelRect(0, 0, Size, Size), buffer, pixels.Length, Size * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    /// <summary>数一数偏红与偏灰的像素。BGRA 顺序，见 RenderTargetBitmap 的默认格式</summary>
    private static (int Red, int Gray) CountColors(byte[] pixels)
    {
        int red = 0;
        int gray = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            byte a = pixels[i + 3];
            if (a < 128) continue;
            if (r > 180 && g < 100 && b < 100) red++;
            else if (r is > 170 and < 240 && Math.Abs(r - g) < 20 && Math.Abs(g - b) < 20) gray++;
        }

        return (red, gray);
    }
}
