using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 名字 + 时间这类大小字并排：各段文字与圆点都按整行竖直居中（不顶对齐、不按基线）
/// </summary>
[Collection(HeadlessCollection.Name)]
public class CenteredStackPanelTests
{
    [Fact]
    public void MixedFontSizes_AndDots_AllCenterOnTheRow() => HeadlessUi.Run(() =>
    {
        TextBlock name = new() { Text = "OP-01 初春", FontSize = 14 };
        TextBlock time = new() { Text = "2026/10/08 10:13", FontSize = 12 };
        TextBlock tag = new() { Text = "已进群", FontSize = 11 };
        Border dot = new() { Width = 8, Height = 8 };
        CenteredStackPanel row = new() { Spacing = 8, Children = { dot, name, time, tag } };
        Window window = new()
        {
            Width = 500, Height = 80,
            Content = new Border { Height = 40, VerticalAlignment = VerticalAlignment.Top, Child = row }
        };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        double rowCenter = row.Bounds.Height / 2;
        // 取整到像素后，奇偶高度差最多半像素
        foreach (Control child in new Control[] { name, time, tag, dot })
            Assert.Equal(rowCenter, child.Bounds.Top + child.Bounds.Height / 2, 0.5);
        Assert.True(time.Bounds.Left > name.Bounds.Right && tag.Bounds.Left > time.Bounds.Right);
        window.Close();
    });
}
