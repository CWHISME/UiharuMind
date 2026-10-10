using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 探针：ScrollViewer 默认 <c>BringIntoViewOnFocusChange=true</c>，点按「部分可见」的按钮
/// 会把焦点元素强制滚进视野——用户在滑动中点到半截露出的条目时，滚动条就自己跳走。
/// 群聊右栏（成员 + 产物区）正是不设防的默认值；修复 = 关掉它，本测试把两条路都钉住。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ScrollViewerFocusJumpTests
{
    [Fact]
    public void DefaultTrue_ClickingPartiallyVisibleButton_JumpsTheOffset()
    {
        HeadlessUi.Run(() =>
        {
            (Window window, ScrollViewer scroll, Button target) = Build(bringIntoViewOnFocusChange: null);
            double before = scroll.Offset.Y;

            Click(window, target);

            Assert.Same(target, window.FocusManager?.GetFocusedElement());
            Assert.True(Math.Abs(scroll.Offset.Y - before) > 1,
                $"默认 true 应把部分可见的焦点元素滚进视野：before={before}, after={scroll.Offset.Y}");
            window.Close();
        });
    }

    [Fact]
    public void ExplicitFalse_ClickingPartiallyVisibleButton_KeepsTheOffset()
    {
        HeadlessUi.Run(() =>
        {
            (Window window, ScrollViewer scroll, Button target) = Build(bringIntoViewOnFocusChange: false);
            double before = scroll.Offset.Y;

            Click(window, target);

            Assert.Same(target, window.FocusManager?.GetFocusedElement());
            Assert.Equal(before, scroll.Offset.Y, 3);
            window.Close();
        });
    }

    /// <summary>
    /// 视口高 200，内容 940。target 放在 y=[300,340]：
    /// 把 Offset 停在 310，target 只剩底部 30px 露在视口内，其余被视口上沿切掉——正是滑动中常见的「半截」状态。
    /// 此时点它，焦点接管后默认行为会滚动到整个按钮可见（Offset 回到 [140,300]）
    /// </summary>
    private static (Window, ScrollViewer, Button) Build(bool? bringIntoViewOnFocusChange)
    {
        StackPanel stack = new();
        for (int i = 0; i < 10; i++) stack.Children.Add(new Border { Height = 30 });
        Button target = new() { Height = 40, Content = "target" };
        stack.Children.Add(target);
        for (int i = 0; i < 20; i++) stack.Children.Add(new Border { Height = 30 });

        ScrollViewer scroll = new() { Content = stack };
        if (bringIntoViewOnFocusChange is { } value) scroll.BringIntoViewOnFocusChange = value;

        Window window = new() { Width = 300, Height = 200, Content = scroll };
        window.Show();
        Pump(window);

        scroll.Offset = new Vector(0, 310);
        Pump(window);

        return (window, scroll, target);
    }

    private static void Click(Window window, Button target)
    {
        Point? center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window);
        Assert.NotNull(center);

        window.MouseMove(center.Value);
        window.MouseDown(center.Value, MouseButton.Left);
        for (int i = 0; i < 2; i++) Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.MouseUp(center.Value, MouseButton.Left);
        for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();
    }
}
