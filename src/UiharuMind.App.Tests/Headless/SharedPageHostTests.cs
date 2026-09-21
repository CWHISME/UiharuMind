using Avalonia.Controls;
using UiharuMind.Shared.Shell;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 常驻页面视图在新旧主窗口之间迁移：页面视图是单例（随 <c>MainViewModel</c> 常驻），
/// 旧主窗口被窗口缓存淘汰/超时真关后，视图还挂在那个已死的 <c>PageHost</c> 上。
/// 新窗口只查自己名下有没有就直接 <c>Add</c>，会抛
/// <c>InvalidOperationException: already has a visual parent</c>，整个应用按未处理异常退出。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SharedPageHostTests
{
    [Fact]
    public void AttachSharedView_StealsViewFromDeadHost()
    {
        HeadlessUi.Run(() =>
        {
            Panel oldHost = new() { Name = "PageHost" };
            Panel newHost = new() { Name = "PageHost" };
            Control view = new UserControl();

            MainView.AttachSharedView(oldHost, view);
            Assert.Contains(view, oldHost.Children);

            Exception? error = Record.Exception(() => MainView.AttachSharedView(newHost, view));
            Assert.Null(error);
            Assert.Contains(view, newHost.Children);
            Assert.DoesNotContain(view, oldHost.Children);
        });
    }

    [Fact]
    public void AttachSharedView_SameHostIsIdempotent()
    {
        HeadlessUi.Run(() =>
        {
            Panel host = new() { Name = "PageHost" };
            Control view = new UserControl();

            MainView.AttachSharedView(host, view);
            Exception? error = Record.Exception(() => MainView.AttachSharedView(host, view));
            Assert.Null(error);
            Assert.Single(host.Children);
        });
    }
}
