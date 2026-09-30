using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using UiharuMind.Shared.Spinner;
using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 转圈图标与共用时间轴：同一时刻所有实例（以及托盘）读到同一相位，隐藏的实例不逐帧刷新
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SpinnerIconTests : IDisposable
{
    private const int HostSize = 96;
    private const double MaxCentroidDrift = 0.05; //像素；改前实测 24px 下 0.29、64px 下 0.32

    private readonly Func<double> _originalElapsed = SpinClock.ElapsedMs;
    private double _now;

    public SpinnerIconTests() => SpinClock.ElapsedMs = () => _now;

    public void Dispose() => SpinClock.ElapsedMs = _originalElapsed;

    [Fact]
    public void Angle_IsContinuous_AndWrapsAfterOneRevolution()
    {
        _now = 0;
        Assert.Equal(0f, SpinClock.Angle);

        _now = SpinClock.FrameCount * SpinClock.IntervalMs / 4.0;
        Assert.Equal(90f, SpinClock.Angle, 0.01f);

        _now = SpinClock.FrameCount * SpinClock.IntervalMs + 50;
        Assert.Equal(15f, SpinClock.Angle, 0.01f);
    }

    [Fact]
    public void Frame_AdvancesOncePerInterval_OnTheSameAxisAsAngle()
    {
        _now = SpinClock.IntervalMs * 2.5;
        Assert.Equal(2, SpinClock.Frame);

        _now = SpinClock.IntervalMs * SpinClock.FrameCount / 4.0;
        Assert.Equal(SpinClock.FrameCount / 4, SpinClock.Frame);
        Assert.Equal(90f, SpinClock.Angle, 0.01f);
    }

    /// <summary>托盘换图对准帧边界：不论此刻在一帧里的哪儿，下一次触发都落在下一帧开头之后一点点</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.99)]
    public void NextTick_LandsJustAfterTheNextFrameBoundary(double intoFrame)
    {
        _now = SpinClock.IntervalMs * (5 + intoFrame);

        _now += SpinClock.DelayToNextFrameMs();

        Assert.Equal(6, SpinClock.Frame);
        Assert.InRange(_now % SpinClock.IntervalMs, 0.5, 5);
    }

    [Fact]
    public void TwoIcons_ReadTheSamePhase_WhateverTimeTheyWereAttached() => HeadlessUi.Run(() =>
    {
        SpinnerIcon early = new();
        StackPanel panel = new() { Children = { early } };
        Window window = new() { Content = panel };
        window.Show();
        _now = 470;

        SpinnerIcon late = new();
        panel.Children.Add(late);
        early.Sync();

        Assert.Equal(SpinClock.Angle, AngleOf(late));
        Assert.Equal(AngleOf(early), AngleOf(late));
        window.Close();
    });

    /// <summary>
    /// 转到任何角度，画出来的花的重心都钉在控件中心：绕 alpha 质心转，且质心摆位不被取整。
    /// 实测不透明度蒙版会把位置取整到整像素、质心少算半格，两样都会让重心画出零点几像素的小圈
    /// </summary>
    [Theory]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(64)]
    public void Centroid_StaysAtTheCenter_AtEveryAngle(int size) => HeadlessUi.Run(() =>
    {
        SpinnerIcon icon = new() { Width = size, Height = size };
        Panel host = HostOf(icon);
        Point center = icon.Bounds.Center;

        List<string> drifts = [];
        foreach (double angle in new[] { 0, 30, 45, 90, 135, 180, 225, 270, 315 })
        {
            ((RotateTransform)icon.RenderTransform!).Angle = angle;
            using SKBitmap rendered = Render(host);
            (float x, float y) = SkiaBitmapUtils.AlphaCentroid(rendered);
            double drift = Math.Sqrt(Math.Pow(x - center.X, 2) + Math.Pow(y - center.Y, 2));
            drifts.Add($"{angle}°: {drift:F3}");
            Assert.True(drift < MaxCentroidDrift, string.Join(", ", drifts));
        }
    });

    /// <summary>不设任何样式就是固定的「在跑」蓝</summary>
    [Fact]
    public void Renders_InRunningColor_WithoutAnyStyle() => HeadlessUi.Run(() =>
    {
        using SKBitmap rendered = Render(HostOf(new SpinnerIcon { Width = 24, Height = 24 }));

        SKColor opaque = Enumerable.Range(0, rendered.Height)
            .SelectMany(y => Enumerable.Range(0, rendered.Width).Select(x => rendered.GetPixel(x, y)))
            .MaxBy(x => x.Alpha);
        Assert.Equal(255, opaque.Alpha);
        Assert.Equal((SpinnerIcon.RunningColor.R, SpinnerIcon.RunningColor.G, SpinnerIcon.RunningColor.B),
            (opaque.Red, opaque.Green, opaque.Blue));
    });

    [Fact]
    public void HiddenAncestor_StopsAnimating_UntilShownAgain() => HeadlessUi.Run(() =>
    {
        SpinnerIcon icon = new();
        StackPanel panel = new() { Children = { icon } };
        Window window = new() { Content = panel };
        window.Show();
        Assert.True(icon.IsSpinning);

        panel.IsVisible = false;
        Assert.False(icon.IsSpinning);

        _now = 600;
        panel.IsVisible = true;
        Assert.True(icon.IsSpinning);
        Assert.Equal(180f, AngleOf(icon), 0.01f);
        window.Close();
    });

    [Fact]
    public void Detached_StopsAnimating() => HeadlessUi.Run(() =>
    {
        SpinnerIcon icon = new();
        Window window = new() { Content = icon };
        window.Show();
        Assert.True(icon.IsSpinning);

        window.Content = null;

        Assert.False(icon.IsSpinning);
        window.Close();
    });

    private static double AngleOf(SpinnerIcon icon) => ((RotateTransform)icon.RenderTransform!).Angle;

    private static Panel HostOf(SpinnerIcon icon)
    {
        Panel host = new() { Width = HostSize, Height = HostSize, Children = { icon } };
        host.Measure(new Size(HostSize, HostSize));
        host.Arrange(new Rect(0, 0, HostSize, HostSize));
        return host;
    }

    private static SKBitmap Render(Visual host)
    {
        using RenderTargetBitmap target = new(new PixelSize(HostSize, HostSize), new Vector(96, 96));
        target.Render(host);
        using MemoryStream stream = new();
        target.Save(stream);
        return SKBitmap.Decode(stream.ToArray());
    }
}
