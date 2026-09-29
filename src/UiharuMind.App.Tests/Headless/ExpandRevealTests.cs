using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 展开容器的三个口径：初始即展开不播动画；展开时高度逐帧增长（补底才顺滑）；收起后彻底退出布局
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ExpandRevealTests
{
    private const double ContentHeight = 100;

    [Fact]
    public void InitiallyOpen_IsFullHeightImmediately() => HeadlessUi.Run(() =>
    {
        (Window window, ExpandReveal reveal) = Show(isOpen: true);

        Assert.True(reveal.IsVisible);
        Assert.Equal(ContentHeight, reveal.Bounds.Height);
        window.Close();
    });

    [Fact]
    public void InitiallyClosed_TakesNoLayoutSpace() => HeadlessUi.Run(() =>
    {
        (Window window, ExpandReveal reveal) = Show(isOpen: false);

        Assert.False(reveal.IsVisible);
        Assert.Equal(0, reveal.Bounds.Height);
        window.Close();
    });

    [Fact]
    public void Open_GrowsThroughIntermediateHeights() => HeadlessUi.Run(() =>
    {
        (Window window, ExpandReveal reveal) = Show(isOpen: false);

        reveal.IsOpen = true;
        List<double> heights = PumpUntil(window, reveal, () => reveal.Bounds.Height >= ContentHeight);

        Assert.Equal(ContentHeight, reveal.Bounds.Height);
        Assert.Contains(heights, h => h > 0 && h < ContentHeight);
        Assert.Equal(heights.OrderBy(h => h), heights);
        window.Close();
    });

    [Fact]
    public void Close_ShrinksThenLeavesLayout() => HeadlessUi.Run(() =>
    {
        (Window window, ExpandReveal reveal) = Show(isOpen: true);

        reveal.IsOpen = false;
        List<double> heights = PumpUntil(window, reveal, () => !reveal.IsVisible);

        Assert.False(reveal.IsVisible);
        Assert.Contains(heights, h => h > 0 && h < ContentHeight);
        window.Close();
    });

    [Fact]
    public void Open_AtBottom_KeepsViewportPinnedEveryFrame() => HeadlessUi.Run(() =>
    {
        (Window window, ScrollViewer scroll, ExpandReveal reveal) = ShowInScroll();
        scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
        Pump(window);

        reveal.IsOpen = true;
        List<double> gaps = [];
        PumpUntil(window, reveal, () => reveal.Bounds.Height >= ContentHeight,
            () => gaps.Add(scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y));

        Assert.NotEmpty(gaps);
        Assert.All(gaps, gap => Assert.InRange(gap, -0.5, 0.5));
        window.Close();
    });

    [Fact]
    public void Open_NotAtBottom_LeavesScrollPositionAlone() => HeadlessUi.Run(() =>
    {
        (Window window, ScrollViewer scroll, ExpandReveal reveal) = ShowInScroll();
        scroll.Offset = new Vector(0, 50);
        Pump(window);

        reveal.IsOpen = true;
        PumpUntil(window, reveal, () => reveal.Bounds.Height >= ContentHeight);

        Assert.Equal(50, scroll.Offset.Y);
        window.Close();
    });

    private static (Window, ScrollViewer, ExpandReveal) ShowInScroll()
    {
        ExpandReveal reveal = new() { Child = new Border { Height = ContentHeight } };
        ScrollViewer scroll = new()
        {
            Content = new StackPanel { Children = { new Border { Height = 400 }, reveal } },
        };
        Window window = new() { Width = 300, Height = 200, Content = scroll };
        window.Show();
        Pump(window);
        return (window, scroll, reveal);
    }

    private static (Window, ExpandReveal) Show(bool isOpen)
    {
        ExpandReveal reveal = new() { IsOpen = isOpen, Child = new Border { Height = ContentHeight } };
        Window window = new() { Width = 300, Height = 300, Content = new StackPanel { Children = { reveal } } };
        window.Show();
        Pump(window);
        return (window, reveal);
    }

    private static List<double> PumpUntil(Window window, ExpandReveal reveal, Func<bool> done,
        Action? onFrame = null)
    {
        List<double> heights = [];
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (!done() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(16);
            Pump(window);
            heights.Add(reveal.Bounds.Height);
            onFrame?.Invoke();
        }

        Assert.True(done(), "过渡应在 3 秒内走完");
        return heights;
    }

    private static void Pump(Window window)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
