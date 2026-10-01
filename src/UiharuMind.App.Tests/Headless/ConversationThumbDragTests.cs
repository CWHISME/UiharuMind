using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 按住会话流的滚动条滑块往上拖到顶：停着不动也一窗一窗往前翻，滑块始终留在指针下面，往回拖照样跟手。
///
/// 拖着时若按滚轮那条路补偿 Offset，滑块会被推离指针（滑块位置是 Offset 的比例，Offset 被补成前插高度），
/// 而 Thumb 保留着按下时的抓点——指针得先追上被推开的那段才拖得动，几次之后撞到屏幕顶，
/// 用户看到的是「进度条跑到底下、再也拖不动」。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ConversationThumbDragTests
{
    [Fact]
    public void HoldingThumbAtTop_KeepsPagingEarlier_WithThumbUnderPointer() => HeadlessUi.RunAsync(async () =>
    {
        ChatSession session = SessionManager.Instance.StartNewSession(new CharacterData { CharacterName = "drag" });
        try
        {
            string filler = string.Concat(Enumerable.Repeat("写长一点好让每条消息都有真实高度。", 12));
            for (int i = 0; i < 160; i++)
            {
                session.History.Add(new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"第 {i} 条：{filler}"));
            }

            SessionManager.Instance.Save(session);

            using ConversationViewModel vm = new(new RecordingMessageService()) { IsDisplayed = true };
            ConversationView view = new();
            Window window = new() { Width = 900, Height = 700, Content = view };
            window.Show();
            Task load = vm.LoadSessionAsync(SessionManager.Instance.GetMeta(session.SessionId));
            view.DataContext = vm;
            await load;
            for (int i = 0; i < 10; i++)
            {
                Settle(window);
                await Task.Delay(10);
            }

            Assert.True(vm.HasEarlierMessages);
            ScrollViewer viewer = view.GetVisualDescendants().OfType<ScrollViewer>().First(x => x.Name == "Viewer");
            Thumb thumb = OwnThumb(viewer);

            // 拖到顶按住不动:一窗一窗往前翻,而每一次 Offset 都还在顶部——滑块没被推离指针
            int before = vm.Items.Count;
            Point pointer = DragToTop(window, thumb);
            int pages = 0;
            for (int i = 0; i < 20 && pages < 3; i++)
            {
                int count = vm.Items.Count;
                await Task.Delay(120);
                Settle(window);
                Assert.True(viewer.Offset.Y <= 32, $"按住期间滑块被推离了顶部:Offset={viewer.Offset.Y:F0}");
                if (vm.Items.Count > count) pages++;
            }

            Assert.True(pages >= 2, $"按住停在顶部没有连续往前翻:{before} -> {vm.Items.Count}");

            // 往回拖照样跟手。指针刚才越过了顶(滑块早被钳在轨道顶),得先回到抓点以下才重新带动滑块,
            // 那是滚动条的常规行为;拖到窗口下部还不动才是抓点错位
            Point back = new(pointer.X, window.Height - 50);
            window.MouseMove(back, RawInputModifiers.LeftMouseButton);
            Settle(window);
            Assert.True(viewer.Offset.Y > 32, $"往回拖不动:Offset={viewer.Offset.Y:F0}");
            window.MouseUp(back, MouseButton.Left);

            window.Close();
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    });

    /// <summary>会话流自己那根滚动条的滑块（卡片里嵌着的滚动容器也有滑块，不能取第一个）</summary>
    private static Thumb OwnThumb(ScrollViewer viewer)
    {
        ScrollBar bar = viewer.GetVisualDescendants().OfType<ScrollBar>()
            .First(x => x.Orientation == Orientation.Vertical && ReferenceEquals(x.TemplatedParent, viewer));
        return bar.GetVisualDescendants().OfType<Thumb>().First();
    }

    /// <summary>抓住滑块中点，一小步一小步拖到窗口顶，不松手</summary>
    /// <returns>指针最后停的位置</returns>
    private static Point DragToTop(Window window, Thumb thumb)
    {
        Point pointer = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        window.MouseMove(pointer);
        window.MouseDown(pointer, MouseButton.Left);
        while (pointer.Y > 0)
        {
            pointer = new Point(pointer.X, Math.Max(0, pointer.Y - 8));
            window.MouseMove(pointer, RawInputModifiers.LeftMouseButton);
            Settle(window);
        }

        return pointer;
    }

    private static void Settle(Window window)
    {
        for (int i = 0; i < 2; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
