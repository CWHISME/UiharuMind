using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.Features.Conversation.Pages;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 主题六 B：右栏物化后置。
/// Content 不绑定——属性变化排 Background 物化，首帧只出壳；版本号作废过期排队（切会话/页签时
/// 旧物化不能串到新会话）。这里钉住「不立即应用」与「新值作废旧值」两条，防止以后有人改回直接绑定。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ConversationPageShellMaterializeTests
{
    [Fact]
    public void RightPaneContent_AppliesOnBackground_NotImmediately() => HeadlessUi.Run(() =>
    {
        ConversationPageShell shell = new();
        UserControl content = new() { Name = "pane" };

        shell.RightPaneContent = content;

        // 未物化：Content 要等 Background 队列跑过才应用
        Assert.Null(shell.RightPaneHost.Content);

        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        Assert.Same(content, shell.RightPaneHost.Content);
    });

    [Fact]
    public void RightPaneContent_NewerValue_InvalidatesOlderQueued() => HeadlessUi.Run(() =>
    {
        ConversationPageShell shell = new();
        UserControl old = new() { Name = "old" };
        UserControl newer = new() { Name = "newer" };

        shell.RightPaneContent = old;
        shell.RightPaneContent = newer; //第二次设置：旧排队作废

        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        Assert.Same(newer, shell.RightPaneHost.Content);
    });

    [Fact]
    public void RightPaneContent_UserHitBeforeMaterialize_AppliesImmediately() => HeadlessUi.Run(() =>
    {
        ConversationPageShell shell = new();
        UserControl content = new() { Name = "pane" };

        shell.RightPaneContent = content;
        Assert.Null(shell.RightPaneHost.Content);

        // 模拟用户命中右栏（等待策略：同步物化）
        shell.SyncMaterializeNow();

        Assert.Same(content, shell.RightPaneHost.Content);
    });
}
