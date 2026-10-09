using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 名字 + 时间这类大小字并排：落在同一条基线上，圆点一类非文字子项按整行居中
/// </summary>
[Collection(HeadlessCollection.Name)]
public class BaselineStackPanelTests
{
    [Fact]
    public void MixedFontSizes_ShareOneBaseline_AndOtherChildrenCenter() => HeadlessUi.Run(() =>
    {
        TextBlock name = new() { Text = "OP-01 初春", FontSize = 14 };
        TextBlock time = new() { Text = "2026/10/08 10:13", FontSize = 12 };
        TextBlock tag = new() { Text = "已进群", FontSize = 11 };
        Border dot = new() { Width = 8, Height = 8 };
        BaselineStackPanel row = new() { Spacing = 8, Children = { dot, name, time, tag } };
        Window window = new()
        {
            Width = 500, Height = 80,
            Content = new Border { Height = 40, VerticalAlignment = VerticalAlignment.Top, Child = row }
        };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        double Baseline(TextBlock text) => text.Bounds.Top + text.TextLayout.TextLines[0].Baseline;
        // 布局取整后只剩各字号基线的小数部分
        Assert.Equal(Baseline(name), Baseline(time), 0.5);
        Assert.Equal(Baseline(name), Baseline(tag), 0.5);
        Assert.Equal(row.Bounds.Height / 2, dot.Bounds.Top + dot.Bounds.Height / 2, 0.5);
        Assert.True(time.Bounds.Left > name.Bounds.Right && tag.Bounds.Left > time.Bounds.Right);
        window.Close();
    });
}
