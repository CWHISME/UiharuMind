using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 切走的会话要能被回收。页面壳会丢掉闲着的视图模型，但只要有一个气泡被长寿对象挂住，
/// 气泡 → 卡片 → 条目 → 条目动作 → 视图模型这条链就把整个会话留在堆上——实测来回切 5 个长会话，
/// 强制 GC 之后托管堆每圈仍涨约 18MB，元凶是气泡在构造里订阅了应用的主题事件、从不退订。
/// 「滚远收卡、滚回重建」每重建一次也多漏一个。
///
/// 只测气泡本身而不测整个视图模型：后者在全量测试里会被别的测试留下的窗口与静态状态交叉挂住，
/// 判不出是谁的锅。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ConversationViewReleaseTests
{
    [Fact]
    public void BubbleRemovedFromTree_CanBeCollected() => HeadlessUi.Run(() =>
    {
        StackPanel host = new();
        Window window = new() { Width = 600, Height = 400, Content = host };
        window.Show();

        WeakReference released = ShowThenRemove(window, host);
        Settle(window);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(released.IsAlive, "离开视觉树的气泡仍被挂住");
        window.Close();
    });

    /// <summary>不内联：局部变量留在本帧里，调用方的 GC 才看不到它们</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ShowThenRemove(Window window, StackPanel host)
    {
        SimpleMarkdownViewer bubble = new() { IsPlaintext = false };
        bubble.ForceSetText("## 标题\n\n正文\n\n```csharp\nvar x = 1;\n```\n");
        host.Children.Add(bubble);
        Settle(window);
        bubble.RealizeNow();
        Settle(window);

        host.Children.Remove(bubble);
        return new WeakReference(bubble);
    }

    private static void Settle(Window window)
    {
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            // 推一帧渲染:没推之前合成批次里还扣着刚摘下的可视对象,会被误判成泄漏
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
