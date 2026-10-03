using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation.Group;

namespace UiharuMind.App.Tests.Group;

/// <summary>
/// 主持人下拉与徽章一致性：二次构造（关了再开）必须两边都指向同一位；
/// 取消更换必须回到落盘真相，不能停在“上面空、下面有徽章”的分叉态。
/// </summary>
public class GroupHostSelectionTests : IDisposable
{
    private readonly RecordingMessageService _messages = new();
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

    /// <summary>二次构造：下拉与徽章都指向主持人，不会出现“上面无、下面有徽章”</summary>
    [Fact]
    public void Reopen_ShowsSameHost_TopAndBadges()
    {
        ChatSession group = NewGroupWithHost("g-host-reopen", hostIndex: 1);
        string hostId = group.GroupHostSessionId!;

        GroupMembersViewData first = NewMembers(group);
        GroupMembersViewData second = NewMembers(group);

        Assert.Equal(hostId, first.SelectedHost?.SessionId);
        Assert.Equal(hostId, second.SelectedHost?.SessionId);
        Assert.Equal([false, true, false], second.Members.Select(x => x.IsHost).ToList());
    }

    /// <summary>取消更换：下拉回到主持人，群壳与徽章原样，不弹第二遍</summary>
    [Fact]
    public async Task Cancel_Change_RevertsToPersistedHost()
    {
        ChatSession group = NewGroupWithHost("g-host-cancel", hostIndex: 1);
        string hostId = group.GroupHostSessionId!;
        MarkRun(group);
        _messages.ConfirmResult = false;
        GroupMembersViewData members = NewMembers(group);

        members.SelectedHost = members.HostOptions[0]; //换成“无”
        await WaitForConfirmsAsync(1);

        Assert.Equal(hostId, members.SelectedHost?.SessionId);
        Assert.Same(members.HostOptions.First(x => x.SessionId == hostId), members.SelectedHost);
        Assert.Equal(hostId, group.GroupHostSessionId);
        Assert.Equal([false, true, false], members.Members.Select(x => x.IsHost).ToList());
        Assert.Equal(1, _messages.ConfirmCount);
    }

    /// <summary>推回来的是落盘真相（取消回滚、模板抖动）：静默对齐，不再弹确认</summary>
    [Fact]
    public async Task RevertToTruth_DoesNotAskAgain()
    {
        ChatSession group = NewGroupWithHost("g-host-truth", hostIndex: 0);
        MarkRun(group);
        _messages.ConfirmResult = false;
        GroupMembersViewData members = NewMembers(group);
        GroupHostChoice none = members.HostOptions[0];

        members.SelectedHost = none; //第一次：真改，弹一次，取消后回到主持人
        await WaitForConfirmsAsync(1);

        members.SelectedHost = none; //第二次：还是真改，再弹一次
        members.SelectedHost = members.HostOptions.First(x => x.SessionId == group.GroupHostSessionId);
        await WaitForConfirmsAsync(2); //推回来的是落盘真相：静默对齐，不再弹第三次
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(2, _messages.ConfirmCount);
        Assert.Equal(group.GroupHostSessionId, members.SelectedHost?.SessionId);
    }

    /// <summary>同一个人改名：SessionId 没变，不当成换主持人、不弹窗</summary>
    [Fact]
    public void SameHost_Renamed_DoesNotTriggerConfirm()
    {
        ChatSession group = NewGroupWithHost("g-host-rename", hostIndex: 1);
        string hostId = group.GroupHostSessionId!;
        MarkRun(group);
        GroupMembersViewData members = NewMembers(group);

        members.SelectedHost = new GroupHostChoice(hostId, "改了个名");

        Assert.Empty(_messages.Confirms);
        Assert.Equal(hostId, members.SelectedHost?.SessionId);
        Assert.Equal(hostId, group.GroupHostSessionId);
    }

    private ChatSession NewGroupWithHost(string name, int hostIndex)
    {
        ChatSession group = GroupChatSessions.Create(name, false,
            [new CharacterData { CharacterName = "A" }, new CharacterData { CharacterName = "B" },
                new CharacterData { CharacterName = "C" }],
            null, null, new GroupSchedule(EGroupScheduleMode.Parallel, EGroupStopPolicy.Conservative, hostIndex));
        _ownedGroups.Add(group.SessionId);
        return group;
    }

    private GroupMembersViewData NewMembers(ChatSession group) =>
        new(group, new GroupChangePrompts(_messages));

    private static void MarkRun(ChatSession group) =>
        SessionManager.Instance.Load(group.GroupMemberSessionIds[0])!.TotalInputTokens = 100;

    private async Task WaitForConfirmsAsync(int count)
    {
        for (int i = 0; i < 100 && _messages.ConfirmCount < count; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(_messages.ConfirmCount >= count, "确认框一直没弹出来");
        await Task.Delay(50, TestContext.Current.CancellationToken); //fire-and-forget 的收尾（回滚/写回）再落定
    }
}
