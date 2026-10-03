using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation.Group;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 主持人下拉挂的是真 ComboBox：切会话/名单刷新时 ItemsSource 与 SelectedItem 先后求值，
/// 控件自己把选中项清掉又推回来，不能被当成用户换主持人——否则一切换就弹窗“换成无吗”。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class GroupHostComboBoxTests : IDisposable
{
    private readonly List<string> _ownedGroups = [];

    public void Dispose()
    {
        foreach (string groupId in _ownedGroups)
        {
            foreach (ChatSessionMeta member in SessionManager.Instance.GetGroupMembers(groupId))
                SessionManager.Instance.Delete(member.SessionId);
            SessionManager.Instance.Delete(groupId);
        }
    }

    /// <summary>首次挂载真面板：下拉显示主持人，不弹确认</summary>
    [Fact]
    public void FirstBind_ShowsHost_WithoutPrompt()
    {
        HeadlessUi.Run(() =>
        {
            (Window window, GroupMembersViewData members, RecordingMessageService messages) = ShowPanel(NewGroupWithHost());
            try
            {
                ComboBox combo = FindCombo(window);
                string? hostId = members.SelectedHost?.SessionId;

                Assert.NotNull(hostId);
                Assert.Equal(hostId, (combo.SelectedItem as GroupHostChoice)?.SessionId);
                Assert.Equal([false, true, false], members.Members.Select(x => x.IsHost).ToList());
                Assert.Empty(messages.Confirms);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>二次打开（同一群的新视图，名单刷新/切回会话走这条）：只换 DataContext，不弹确认</summary>
    [Fact]
    public void RebindNewView_NoPrompt_StaysOnHost()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession group = NewGroupWithHost();
            (Window window, GroupMembersViewData _, RecordingMessageService messages) = ShowPanel(group);
            try
            {
                GroupMembersPanel panel = FindPanel(window);
                GroupMembersViewData fresh = NewMembers(group, messages);
                panel.DataContext = fresh;
                Pump(window);

                ComboBox combo = FindCombo(window);
                Assert.Empty(messages.Confirms);
                Assert.Equal(group.GroupHostSessionId, fresh.SelectedHost?.SessionId);
                Assert.Equal(group.GroupHostSessionId, (combo.SelectedItem as GroupHostChoice)?.SessionId);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>控件自己抖动（切会话/模板重建把选中项清掉推回来）：不关下拉就不算数，不弹确认</summary>
    [Fact]
    public void ControlChurn_WithoutDropDownClose_NeverCommits()
    {
        HeadlessUi.Run(() =>
        {
            (Window window, GroupMembersViewData members, RecordingMessageService messages) = ShowPanel(NewGroupWithHost());
            try
            {
                ComboBox combo = FindCombo(window);
                string? hostId = members.SelectedHost?.SessionId;

                // 模拟控件抖动：选中被推到“无”，但用户根本没开过下拉
                combo.SelectedItem = members.HostOptions[0];
                Pump(window);

                Assert.Empty(messages.Confirms);
                Assert.Equal(hostId, members.SelectedHost?.SessionId);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>用户亲手选“无”并关下拉：走确认；选否回滚到主持人</summary>
    [Fact]
    public void UserPick_CommitOnDropDownClose_CancelReverts()
    {
        HeadlessUi.Run(() =>
        {
            (Window window, GroupMembersViewData members, RecordingMessageService messages) = ShowPanel(NewGroupWithHost());
            try
            {
                ComboBox combo = FindCombo(window);
                string? hostId = members.SelectedHost?.SessionId;
                Assert.NotNull(hostId);

                combo.SelectedItem = members.HostOptions[0];
                CloseDropDown(window, combo);
                WaitForConfirms(messages, 1);

                Assert.Equal(hostId, members.SelectedHost?.SessionId);
                Assert.Equal(hostId, (combo.SelectedItem as GroupHostChoice)?.SessionId);
                Assert.Single(members.Members.Where(x => x.IsHost));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void CloseDropDown(Window window, ComboBox combo)
    {
        // 关下拉是路由事件，名字不稳，直接走面板后台代码同一入口（XAML 接线编译期已校验）
        FindPanel(window).OnHostDropDownClosed(combo, EventArgs.Empty);
    }

    private static void WaitForConfirms(RecordingMessageService messages, int count)
    {
        for (int i = 0; i < 100 && messages.ConfirmCount < count; i++)
            Thread.Sleep(10);
        Assert.True(messages.ConfirmCount >= count, "确认框一直没弹出来");
        Thread.Sleep(50); //fire-and-forget 的收尾（回滚/写回）再落定
    }

    /// <summary>在两个群之间来回切：经过别人的 DataContext，不弹确认、不串台</summary>
    [Fact]
    public void SwitchBetweenGroups_NoPrompt_NoLeak()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession groupA = NewGroupWithHost("g-combo-a", 1);
            ChatSession groupB = NewGroupWithHost("g-combo-b", 0);
            (Window window, GroupMembersViewData _, RecordingMessageService messages) = ShowPanel(groupA);
            try
            {
                GroupMembersPanel panel = FindPanel(window);
                panel.DataContext = NewMembers(groupB, messages);
                Pump(window);
                panel.DataContext = NewMembers(groupA, messages);
                Pump(window);

                Assert.Empty(messages.Confirms);
                Assert.Equal(groupA.GroupHostSessionId,
                    (FindCombo(window).SelectedItem as GroupHostChoice)?.SessionId);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private (Window, GroupMembersViewData, RecordingMessageService) ShowPanel(ChatSession group)
    {
        RecordingMessageService messages = new() { ConfirmResult = false };
        GroupMembersViewData members = NewMembers(group, messages);
        GroupMembersPanel panel = new() { Width = 300, Height = 600, DataContext = members };
        Window window = new() { Width = 400, Height = 700, Content = panel };
        window.Show();
        Pump(window);
        return (window, members, messages);
    }

    private static GroupMembersPanel FindPanel(Window window) =>
        window.GetVisualDescendants().OfType<GroupMembersPanel>().First();

    private static ComboBox FindCombo(Window window) =>
        window.GetVisualDescendants().OfType<ComboBox>().First();

    private static void Pump(Window window)
    {
        for (int i = 0; i < 5; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private ChatSession NewGroupWithHost(string name = "g-combo-host", int hostIndex = 1)
    {
        ChatSession group = GroupChatSessions.Create(name, false,
            [new CharacterData { CharacterName = "A" }, new CharacterData { CharacterName = "B" },
                new CharacterData { CharacterName = "C" }],
            null, null, new GroupSchedule(EGroupScheduleMode.Parallel, EGroupStopPolicy.Conservative, hostIndex));
        _ownedGroups.Add(group.SessionId);
        // 跑过的群换主持人才弹窗：先让一位成员有累计，不跑这条分支测不到
        SessionManager.Instance.Load(group.GroupMemberSessionIds[0])!.TotalInputTokens = 100;
        return group;
    }

    private static GroupMembersViewData NewMembers(ChatSession group, RecordingMessageService messages) =>
        new(group, new GroupChangePrompts(messages));
}
