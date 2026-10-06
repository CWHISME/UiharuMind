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

    /// <summary>
    /// 键盘唤出（Shift+F10 / 菜单键）没有指针位置，要像框架默认那样落在行下方，而不是上一次鼠标停留处。
    /// 关掉之后菜单不能还挂着那一行的条目：用它删掉的会话会被一直留在堆上，直到下次右键
    /// </summary>
    [Fact]
    public void KeyboardContextRequest_OpensBelowRow_AndClosingReleasesItem() => HeadlessUi.Run(() =>
    {
        (Window window, SessionListView view, _) = Show();
        ListBoxItem row = Rows(view)[3];

        row.RaiseEvent(new ContextRequestedEventArgs());
        Settle(window);

        ContextMenu menu = Assert.IsType<ContextMenu>(view.Resources["SessionRowMenu"]);
        Assert.True(menu.IsOpen);
        Assert.Equal(PlacementMode.Bottom, menu.Placement);
        Assert.Same(row.DataContext, menu.DataContext);

        menu.Close();
        Settle(window);
        Assert.Null(menu.DataContext);
        window.Close();
    });

    [Fact]
    public void GroupRowMenu_HidesEditCharacter() => HeadlessUi.Run(() =>
    {
        List<ChatSessionMeta> metas = Metas().ToList();
        metas.Add(new ChatSessionMeta
        {
            SessionId = "g1",
            CharacterId = nameof(DefaultCharacter.ChenXiAgent),
            Title = "测试群",
            IsGroup = true,
            IsAgentGroup = true,
            GroupMemberSessionIds = ["s0"],
            UpdatedAt = DateTimeOffset.Now,
        });
        SessionListModel model = new(EConversationType.Agent, () => metas, action => action(), new RecordingMessageService());
        SessionListView view = new() { DataContext = model, ShowAvatar = false };
        Window window = new() { Width = 300, Height = 600, Content = view };
        window.Show();
        Settle(window);

        ListBoxItem groupRow = Rows(window).Single(x => ((SessionListItem)x.DataContext!).IsGroup);
        ListBoxItem plainRow = Rows(window).First(x => !((SessionListItem)x.DataContext!).IsGroup);
        ContextMenu menu = Assert.IsType<ContextMenu>(view.Resources["SessionRowMenu"]);
        MenuItem edit = Assert.IsType<MenuItem>(menu.Items[0]); //编辑角色

        // 普通行:编辑角色可见;群行:同一份菜单、第一项收起,其余仍留
        plainRow.RaiseEvent(new ContextRequestedEventArgs());
        Settle(window);
        Assert.True(edit.IsVisible);
        menu.Close();

        groupRow.RaiseEvent(new ContextRequestedEventArgs());
        Settle(window);
        Assert.Same(groupRow.DataContext, menu.DataContext);
        Assert.Equal(5, menu.Items.Count);
        Assert.False(edit.IsVisible);
        Assert.All(menu.Items.OfType<MenuItem>().Skip(1), x => Assert.True(x.IsVisible));
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

    /// <summary>
    /// 别的会话浮到选中那一行的位置（另一个实例推进了它、后台活落了盘）时，选中不能跟着那个位置走：
    /// 多实例冒烟实测，正等着回复的那个会话被接连换成别的实例刚动过的会话
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)] //首轮发送才懒建的会话不经列表选中,左栏选中一直是空
    public void AnotherSessionFloatingToTheSelectedRow_DoesNotStealTheSelection(bool selected) => HeadlessUi.Run(() =>
    {
        List<ChatSessionMeta> metas = Metas();
        SessionListModel model = new(EConversationType.Agent, () => metas, action => Dispatcher.UIThread.Post(action), new RecordingMessageService());
        SessionListView view = new() { DataContext = model, ShowAvatar = false };
        Window window = new() { Width = 300, Height = 600, Content = view };
        window.Show();
        Settle(window);
        model.SelectWithoutNotifying(selected ? model.Sessions[0] : null);
        Settle(window);
        List<string?> switched = new();
        model.SelectionChanged += x => switched.Add(x?.SessionId);

        foreach (int pick in new[] { 3, 4, 5 })
        {
            ChatSessionMeta moved = metas.First(x => x.SessionId == $"s{pick}");
            metas.Remove(moved);
            metas.Insert(0, new ChatSessionMeta
            {
                SessionId = moved.SessionId, CharacterId = moved.CharacterId, Title = moved.Title, UpdatedAt = DateTimeOffset.Now,
            });
            model.RequestSync();
            Settle(window);
        }

        Assert.Equal(selected ? "s0" : null, model.SelectedSession?.SessionId);
        Assert.Empty(switched);
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
