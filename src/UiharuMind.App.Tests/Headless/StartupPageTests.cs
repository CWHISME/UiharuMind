using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Features.LogViewer;
using UiharuMind.Shared.Shell;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 默认页排在空闲时才落：启动后、空闲前已经跳过的页（开发脚本第一步、托盘点进某页）不能被它盖掉
/// </summary>
[Collection(HeadlessCollection.Name)]
public class StartupPageTests
{
    [Fact]
    public void EarlyJump_IsNotOverriddenByDefaultPage() => HeadlessUi.Run(() =>
    {
        MainViewModel viewModel = new(new ServiceCollection().BuildServiceProvider());

        viewModel.JumpToPage(MenuPages.MenuLogKey);
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<LogPageData>(viewModel.Content);
    });
}
