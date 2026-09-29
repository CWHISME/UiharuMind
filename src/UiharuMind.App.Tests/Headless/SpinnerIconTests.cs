using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using UiharuMind.Shared.Spinner;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 转圈图标与共用时间轴：同一时刻所有实例（以及托盘）读到同一相位，隐藏的实例不逐帧刷新
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SpinnerIconTests : IDisposable
{
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
        _now = 250;
        Assert.Equal(2, SpinClock.Frame);

        _now = 300;
        Assert.Equal(3, SpinClock.Frame);
        Assert.Equal(90f, SpinClock.Angle, 0.01f);
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

    [Fact]
    public void EveryDisplayFrame_FollowsTheClockContinuously() => HeadlessUi.Run(() =>
    {
        SpinnerIcon icon = new();
        Window window = new() { Content = icon };
        window.Show();

        foreach (double ms in new[] { 30.0, 45.0, 61.0 })
        {
            _now = ms;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ms / 1200 * 360, AngleOf(icon), 0.01);
        }

        window.Close();
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
}
