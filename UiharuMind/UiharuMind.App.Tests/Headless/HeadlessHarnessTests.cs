using Avalonia.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>无头会话本身能不能起来。它挂了，下面所有界面测试的失败信息都会没法看</summary>
[Collection(HeadlessCollection.Name)]
public class HeadlessHarnessTests
{
    [Fact]
    public void HeadlessSession_LaysOutAWindow() => HeadlessUi.Run(() =>
    {
        Window window = new() { Width = 400, Height = 300, Content = new TextBlock { Text = "hello" } };
        window.Show();
        window.UpdateLayout();

        Assert.True(window.Bounds.Width > 0);
    });
}
