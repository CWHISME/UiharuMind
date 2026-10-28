using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using UiharuMind.App.Tests.Headless;

namespace UiharuMind.App.Tests.LocalProbe;

/// <summary>临时：验证停靠窗「先撑大再换面板」那套赌注在 SizeToContent 窗口上成不成立。</summary>
[Collection(HeadlessCollection.Name)]
public class TempDockGrowProbeTests
{
    private static Border Chip() => new() { Width = 30, Height = 30, Background = Brushes.Gray };

    [Fact]
    public void SizeToContent_GrowsOnMinWidth_AndFiresSizeChanged() => HeadlessUi.Run(() =>
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        for (int i = 0; i < 4; i++) row.Children.Add(Chip());

        // 与停靠窗同构：面板包在 Border 里，窗口按内容收紧
        var pill = new Border { Padding = new Thickness(3), Child = row };
        var window = new Window
        {
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            Content = pill
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Size before = window.ClientSize;
        int sizeChanged = 0;
        window.SizeChanged += (_, _) => sizeChanged++;

        // ① 内容自己变宽（等价于直接换面板）
        for (int i = 0; i < 6; i++) row.Children.Add(Chip());
        Dispatcher.UIThread.RunJobs();
        Size afterContentGrow = window.ClientSize;
        int sizeChangedByContent = sizeChanged;

        // ② 码里那套：MinWidth 撑大 + UpdateLayout
        sizeChanged = 0;
        window.MinWidth = window.ClientSize.Width + 120;
        window.UpdateLayout();
        Size afterMinWidthSync = window.ClientSize;
        int sizeChangedByMinWidthSync = sizeChanged;
        Dispatcher.UIThread.RunJobs();
        Size afterMinWidthPumped = window.ClientSize;
        int sizeChangedByMinWidthPumped = sizeChanged;

        Console.WriteLine($"[probe] before={before}");
        Console.WriteLine($"[probe] contentGrow={afterContentGrow} sizeChanged={sizeChangedByContent}");
        Console.WriteLine($"[probe] minWidth+UpdateLayout={afterMinWidthSync} sizeChanged={sizeChangedByMinWidthSync}");
        Console.WriteLine($"[probe] minWidth+RunJobs={afterMinWidthPumped} sizeChanged={sizeChangedByMinWidthPumped}");

        window.Close();
    });
}
