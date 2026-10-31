using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.Features.ScreenCapture;
using UiharuMind.Features.ScreenCapture.Drawing;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 标注要真的进到导出的合成图里。屏幕上看得到不代表存得下：<c>RenderTargetBitmap</c> 走的是
/// ImmediateRenderer，它拿控件 Bounds 与裁剪矩形求交，Bounds 为空（0×0）的控件会被整条跳过——
/// 箭头控件当年就是这样「编辑时看得见，双击保存后没了」，而几何单测一条都没红。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ScreenCaptureAnnotationTests
{
    [Fact]
    public void ArrowDrawnInEditor_SurvivesRenderToBitmap() => HeadlessUi.Run(() =>
    {
        var editor = new ImageAnnotationEditor();
        using var source = new WriteableBitmap(new PixelSize(200, 150), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        editor.SetSource(source, 1.0);
        editor.SetTool(DrawingTool.ArrowLine);
        editor.SetColor(Colors.Red);
        editor.SetStrokeThickness(3);

        var window = new Window { Width = 240, Height = 200, Content = editor };
        window.Show();
        Settle(window);

        Canvas canvas = editor.GetVisualDescendants().OfType<Canvas>().First();
        Point from = canvas.TranslatePoint(new Point(20, 120), window)!.Value;
        Point to = canvas.TranslatePoint(new Point(160, 40), window)!.Value;

        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        for (int i = 1; i <= 8; i++) window.MouseMove(from + (to - from) * (i / 8.0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Settle(window);

        ArrowLineControl arrow = Assert.Single(editor.GetVisualDescendants().OfType<ArrowLineControl>());
        Assert.True(arrow.Bounds.Width > 0 && arrow.Bounds.Height > 0,
            $"箭头控件 Bounds 为空（{arrow.Bounds}），导出时会被整条跳过");

        using Bitmap combined = editor.RenderToBitmap();
        Assert.True(CountRedPixels(combined) > 20, "保存出来的合成图里没有箭头");

        window.Close();
    });

    /// <summary>
    /// 命中测试用的是控件局部坐标（框架把点换算到控件内再交给 <c>ICustomHitTest</c>）。
    /// 这条钉住的是「包围盒角落的空白不吞点击、线本身点得中」——坐标口径错了命中区就整体偏移，
    /// 表现为箭头点不中、旁边的空白倒是能点。
    /// </summary>
    [Fact]
    public void ArrowHitTest_FollowsLocalCoordinates() => HeadlessUi.Run(() =>
    {
        var arrow = new ArrowLineControl(2, Colors.Red) { Width = 100, Height = 100 };
        arrow.SetGeometry(new Point(0, 0), new Point(80, 80));
        Canvas.SetLeft(arrow, 100);
        Canvas.SetTop(arrow, 60);

        var canvas = new Canvas { Width = 300, Height = 300, Background = Brushes.Transparent };
        canvas.Children.Add(arrow);
        var window = new Window { Width = 300, Height = 300, Content = canvas };
        window.Show();
        Settle(window);

        // 线中点：画布坐标 (100 + 40, 60 + 40)
        Assert.Same(arrow, window.InputHitTest(new Point(140, 100)));
        // 包围盒右上角 (100 + 80, 60 + 5)：离对角线很远，不该命中
        Assert.NotSame(arrow, window.InputHitTest(new Point(180, 65)));

        window.Close();
    });

    private static void Settle(Window window)
    {
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
        }
    }

    // 无头宿主开了真 Skia，能拿到像素。只数「偏红的点」，不钉坐标：线宽、抗锯齿怎么调都不该让这条红
    private static int CountRedPixels(Bitmap bitmap)
    {
        PixelSize size = bitmap.PixelSize;
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;

        using var decoded = new Bitmap(stream);
        byte[] pixels = new byte[size.Width * size.Height * 4];
        IntPtr buffer = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            decoded.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), buffer, pixels.Length, size.Width * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        int red = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i + 2] > 150 && pixels[i + 1] < 120 && pixels[i] < 120) red++;
        }

        return red;
    }
}
