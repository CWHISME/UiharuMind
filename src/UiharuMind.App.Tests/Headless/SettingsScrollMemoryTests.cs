using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.Core.Configs;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 设置页滚动位置记忆（<see cref="SettingsScrollMemory"/> 附件属性）：
/// 关掉再开一个新实例，位置要能滚回去。这层只在「跨会话/新窗口」有意义，
/// 缓存窗口复用本来位置就在，所以还原只在当前位置还在顶部时发生。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SettingsScrollMemoryTests : IDisposable
{
    private readonly List<Window> _windows = [];

    public void Dispose() => HeadlessUi.Run(() =>
    {
        foreach (Window window in _windows) window.Close();
    });

    [Fact]
    public void ScrollPosition_SavedAndRestoredAcrossReopen()
    {
        HeadlessUi.Run(() =>
        {
            ScrollViewer first = NewScroller();
            ShowInWindow(first);
            first.Offset = new Vector(0, 500);
            Dispatcher.UIThread.RunJobs();
            Assert.True(first.Offset.Y > 0, $"前提：滚动没生效 {first.Offset.Y}");

            // 同进程里再开一个新实例（配置单例还在，相当于跨会话重开）
            ScrollViewer second = NewScroller();
            ShowInWindow(second);
            Dispatcher.UIThread.RunJobs();
            Assert.True(second.Offset.Y > 400, $"还原失败：{second.Offset.Y}");
        });
    }

    [Fact]
    public void ScrollPosition_WithoutKey_DoesNotRestore()
    {
        HeadlessUi.Run(() =>
        {
            // 先往记忆槽里放一笔，再开一个没挂 key 的滚动容器，确认它不会误还原
            ConfigManager.Instance.Setting.SettingsScrollPositions["no-key-test"] = 999;

            var content = new StackPanel();
            for (int i = 0; i < 300; i++)
            {
                content.Children.Add(new TextBlock { Text = $"line {i}" });
            }

            var sv = new ScrollViewer { Content = content };
            ShowInWindow(sv);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, sv.Offset.Y);
        });
    }

    private ScrollViewer NewScroller()
    {
        var content = new StackPanel();
        for (int i = 0; i < 300; i++)
        {
            content.Children.Add(new TextBlock { Text = $"line {i}" });
        }

        var sv = new ScrollViewer { Content = content };
        SettingsScrollMemory.SetScrollKey(sv, "scroll-test-key");
        return sv;
    }

    private void ShowInWindow(ScrollViewer sv)
    {
        var window = new Window { Width = 300, Height = 200, Content = sv };
        _windows.Add(window);
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
