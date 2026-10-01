using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.SessionList;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 左栏会话列表的行模板为了滚动性能把平时看不见的部件都改成用到才建（见 SessionListView.axaml 的注释）。
/// 这里钉住三件因此容易坏的交互：右键菜单认对行、删除按钮悬停才出来且不改行高、批量模式才有勾选框。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SessionListViewTests
{
    private static List<ChatSessionMeta> Metas() => Enumerable.Range(0, 6).Select(i => new ChatSessionMeta
    {
        SessionId = $"s{i}",
        CharacterId = nameof(DefaultCharacter.ChenXiAgent),
        Title = $"会话 {i}",
        UpdatedAt = DateTimeOffset.Now.AddMinutes(-i),
    }).ToList();

    private static (Window Window, SessionListView View, SessionListModel Model) Show()
    {
        SessionListModel model = new(EConversationType.Agent, Metas, action => action(), new RecordingMessageService());
        SessionListView view = new() { DataContext = model, ShowAvatar = false };
        Window window = new() { Width = 300, Height = 600, Content = view };
        window.Show();
        Settle(window);
        return (window, view, model);
    }

    private static List<ListBoxItem> Rows(Visual view) =>
        view.GetVisualDescendants().OfType<ListBoxItem>().Where(x => x.IsVisible).ToList();

    private static Point CenterOf(Visual target, Visual root) =>
        target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), root)!.Value;

    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    [Fact]
    public void RightClick_OpensSharedMenuForThatRow() => HeadlessUi.Run(() =>
    {
        (Window window, SessionListView view, _) = Show();
        ListBoxItem row = Rows(view)[2];

        Point point = CenterOf(row, window);
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Settle(window);

        ContextMenu menu = Assert.IsType<ContextMenu>(view.Resources["SessionRowMenu"]);
        Assert.True(menu.IsOpen);
        Assert.Same(row.DataContext, menu.DataContext);
        menu.Close();
        window.Close();
    });

    [Fact]
    public void DeleteButton_IsBuiltOnHover_WithoutChangingRowHeight() => HeadlessUi.Run(() =>
    {
        (Window window, SessionListView view, _) = Show();
        ListBoxItem row = Rows(view)[1];
        double heightBefore = row.Bounds.Height;
        Assert.Empty(view.GetVisualDescendants().OfType<Button>().Where(x => x.Classes.Contains("session-delete")));

        window.MouseMove(CenterOf(row, window));
        Settle(window);

        Button delete = Assert.Single(view.GetVisualDescendants().OfType<Button>().Where(x => x.Classes.Contains("session-delete")));
        Assert.Same(row, delete.FindAncestorOfType<ListBoxItem>());
        Assert.Equal(heightBefore, row.Bounds.Height, 1); //悬停不许让整行长高、列表跟着跳
        window.Close();
    });

    [Fact]
    public void BatchMode_BuildsCheckBoxes_OnlyWhileOn() => HeadlessUi.Run(() =>
    {
        (Window window, SessionListView view, SessionListModel model) = Show();
        Assert.Empty(view.GetVisualDescendants().OfType<CheckBox>());

        model.IsBatchMode = true;
        Settle(window);
        Assert.Equal(Rows(view).Count, view.GetVisualDescendants().OfType<CheckBox>().Count());

        model.IsBatchMode = false;
        Settle(window);
        Assert.Empty(view.GetVisualDescendants().OfType<CheckBox>());
        window.Close();
    });
}
