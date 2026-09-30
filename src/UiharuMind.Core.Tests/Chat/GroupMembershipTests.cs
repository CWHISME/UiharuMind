using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 建群之后改名单（ADR 0046 修订「建群之后增删成员」）：退群留会话、加回接着听、补历史定游标、谁写入群摘要。
/// 会话一律是临时的，不落盘
/// </summary>
public class GroupMembershipTests
{
    private readonly Dictionary<string, ChatSession> _sessions = new();
    private readonly FakeGroupMemberTurnRunner _runner = new();
    private readonly GroupChatCoordinator _coordinator;
    private readonly ChatSession _group;
    private readonly ChatSession _alice;
    private readonly ChatSession _bob;

    public GroupMembershipTests()
    {
        DefaultCharacterManager.Instance.OnInitialize(); //用户发言的署名取自内置用户卡
        _coordinator = new GroupChatCoordinator(_runner, Load);
        _group = Track(new ChatSession { Title = "会审", IsGroup = true, IsAgentGroup = true, IsTransient = true });
        _alice = Member("Alice");
        _bob = Member("Bob");
        _group.GroupMemberSessionIds = [_alice.SessionId, _bob.SessionId];
    }

    [Fact]
    public void Remove_KeepsTheSession_AndDropsHostAndScene()
    {
        _group.GroupHostSessionId = _bob.SessionId;

        Assert.True(GroupMembership.Remove(_group, _bob, Load));

        Assert.Equal([_alice.SessionId], _group.GroupMemberSessionIds);
        Assert.Null(_group.GroupHostSessionId);
        Assert.True(_bob.HasLeftGroup);
        Assert.Equal(_group.SessionId, _bob.GroupId); //还挂在群上：随群级联删除、右栏点得开
        Assert.Equal("Alice", _group.Description);
        // 场景段只认名单：他之后私聊就是个普通角色
        Assert.Equal("", GroupSceneSource.For(_bob, _group, _ => null, "我"));
        Assert.DoesNotContain("Bob", GroupSceneSource.For(_alice, _group, CharacterOf, "我"));
    }

    [Fact]
    public void Remove_RefusesTheLastMember()
    {
        Assert.True(GroupMembership.Remove(_group, _bob, Load));
        Assert.False(GroupMembership.Remove(_group, _alice, Load));
        Assert.Equal([_alice.SessionId], _group.GroupMemberSessionIds);
    }

    [Fact]
    public async Task RemovedMember_IsNotScheduled_AndCannotPost()
    {
        GroupMembership.Remove(_group, _bob, Load);

        await _coordinator.PostAsync(_group, "大家好");

        Assert.Equal([_alice.SessionId], _runner.Calls.Select(x => x.Member.SessionId));
        Assert.False(_coordinator.TryPostFromMember(_bob.SessionId, "我还在"));
    }

    [Fact]
    public async Task Rejoin_Full_DeliversWhatHeMissed()
    {
        await _coordinator.PostAsync(_group, "第一句");
        GroupMembership.Remove(_group, _bob, Load);
        await _coordinator.PostAsync(_group, "他不在时说的");
        _runner.ClearCalls();

        GroupMembership.Admit(_group, _bob, GroupBackfill.Full, Load);
        await _coordinator.ContinueAsync(_group);

        Assert.False(_bob.HasLeftGroup);
        Assert.Equal([_alice.SessionId, _bob.SessionId], _group.GroupMemberSessionIds);
        string bobInput = _runner.Calls.Single(x => x.Member == _bob).Input;
        Assert.Contains("他不在时说的", bobInput);
        Assert.DoesNotContain("第一句", bobInput); //离开之前听过的不再给
    }

    [Fact]
    public async Task Rejoin_None_StartsFromNow()
    {
        await _coordinator.PostAsync(_group, "第一句");
        GroupMembership.Remove(_group, _bob, Load);
        await _coordinator.PostAsync(_group, "他不在时说的");

        GroupMembership.Admit(_group, _bob, GroupBackfill.None, Load);

        Assert.Equal(_group.History.Count, _bob.GroupCursor);
    }

    [Fact]
    public async Task Summary_RidesOnTheNextDelivery_Once()
    {
        await _coordinator.PostAsync(_group, "第一句");
        ChatSession carol = Member("Carol");
        // 写摘要的人只听到了第一句之前那一截：之后的原文照常投递
        int briefedUpTo = 1;
        GroupMembership.Admit(_group, carol, new GroupBackfill(EGroupBackfill.Summary, "（摘要）要定评审口径", briefedUpTo), Load);
        _runner.ClearCalls();

        await _coordinator.ContinueAsync(_group);

        string carolInput = _runner.Calls.First(x => x.Member == carol).Input;
        Assert.StartsWith("（摘要）要定评审口径\n\n", carolInput);
        Assert.Contains("[Alice]:", carolInput);
        Assert.DoesNotContain("第一句", carolInput);
        Assert.Null(carol.GroupBriefing);

        _runner.ClearCalls();
        await _coordinator.ContinueAsync(_group);
        Assert.All(_runner.Calls.Where(x => x.Member == carol), x => Assert.DoesNotContain("（摘要）", x.Input));
    }

    [Fact]
    public void Summary_WithoutBriefing_FallsBackToNone()
    {
        _group.History.Add(new ChatMessage(ChatRole.User, "第一句"));
        ChatSession carol = Member("Carol");

        GroupMembership.Admit(_group, carol, new GroupBackfill(EGroupBackfill.Summary), Load);

        Assert.Equal(1, carol.GroupCursor);
        Assert.Null(carol.GroupBriefing);
    }

    [Fact]
    public async Task BriefingWriter_IsHost_ThenLatestSpeaker()
    {
        await _coordinator.PostAsync(_group, "大家好"); //Alice 先说、Bob 后说

        Assert.Same(_bob, GroupMembership.PickBriefingWriter(_group, Load));

        _group.GroupHostSessionId = _alice.SessionId;
        Assert.Same(_alice, GroupMembership.PickBriefingWriter(_group, Load));
    }

    [Fact]
    public void BriefingWriter_NobodyWithHistory_IsNull()
    {
        Assert.Null(GroupMembership.PickBriefingWriter(_group, Load));
    }

    private ChatSession? Load(string id) => _sessions.GetValueOrDefault(id);

    private CharacterData? CharacterOf(string id) => Load(id)?.CharacterData;

    private ChatSession Member(string name)
    {
        CharacterData character = new() { CharacterId = name.ToLowerInvariant(), CharacterName = name };
        ChatSession member = new(name, character) { IsTransient = true, GroupId = _group.SessionId };
        return Track(member);
    }

    private ChatSession Track(ChatSession session)
    {
        _sessions[session.SessionId] = session;
        return session;
    }
}
