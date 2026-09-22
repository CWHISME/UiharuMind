using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 空态里的按钮必须点得中：空态 presenter 与消息 <c>ScrollViewer</c> 叠在同一格，
/// 后声明的 Viewer 天然压在上面——空态里第一颗可点的东西（群聊按钮）会被空 Viewer
/// 吃掉点击。以前空态里没有可点的东西，这条路从没被走过。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ConversationEmptyClickTests
{
    [Fact]
    public void EmptyStateButton_ReceivesClicks_OverEmptyMessageList()
    {
        HeadlessUi.Run(() =>
        {
            ConversationViewModel vm = new(); //空会话：无条目、非加载 → 空态 presenter 可见
            ConversationView view = new() { DataContext = vm };
            int clicks = 0;
            Button probe = new() { Content = "probe", Width = 160, Height = 36 };
            probe.Click += (_, _) => clicks++;
            view.EmptyContent = probe;

            Window window = new() { Width = 900, Height = 700, Content = view };
            window.Show();
            for (int i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
                window.UpdateLayout();
            }

            Assert.True(probe.IsEffectivelyVisible);
            Assert.True(probe.Bounds.Width > 0);
            Point? center = probe.TranslatePoint(
                new Point(probe.Bounds.Width / 2, probe.Bounds.Height / 2), window);
            Assert.NotNull(center);

            window.MouseMove(center.Value);
            window.MouseDown(center.Value, MouseButton.Left);
            for (int i = 0; i < 2; i++) Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            window.MouseUp(center.Value, MouseButton.Left);
            for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            Assert.Equal(1, clicks);
            window.Close();
        });
    }
}
