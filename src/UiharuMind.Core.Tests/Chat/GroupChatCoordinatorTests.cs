using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 群聊调度与投递（ADR 0046）。跑模型的那一段换成假的：这里要钉的是「交给谁、交什么、算不算群发言」，
/// 那些对错跟模型无关。会话一律是临时的，不落盘。
/// </summary>
public class GroupChatCoordinatorTests
{
    private readonly Dictionary<string, ChatSession> _sessions = new();
    private readonly FakeRunner _runner = new();
    private readonly GroupChatCoordinator _coordinator;
    private readonly ChatSession _group;
    private readonly ChatSession _alice;
    private readonly ChatSession _bob;

    public GroupChatCoordinatorTests()
    {
        DefaultCharacterManager.Instance.OnInitialize(); //用户发言的署名取自内置用户卡
        _coordinator = new GroupChatCoordinator(_runner, id => _sessions.GetValueOrDefault(id));
        _group = Track(new ChatSession { Title = "会审", IsGroup = true, IsAgentGroup = true, IsTransient = true });
        _alice = Member("Alice");
        _bob = Member("Bob");
        _group.GroupMemberSessionIds = [_alice.SessionId, _bob.SessionId];
    }

    [Fact]
    public async Task Round_DeliversWhatEachMemberHasNotHeard_InSpeakingOrder()
    {
        await _coordinator.PostAsync(_group, "大家好");

        Assert.Equal([_alice.SessionId, _bob.SessionId], _runner.Calls.Select(x => x.Member.SessionId));
        Assert.Contains("大家好", _runner.Calls[0].Input);
        Assert.DoesNotContain("[Alice]", _runner.Calls[0].Input);
        // 后说的人听得到先说的人
        Assert.Contains("[Alice]: Alice 的第 1 次发言", _runner.Calls[1].Input);

        Assert.Equal(3, _group.History.Count);
        Assert.Equal("Alice", _group.History[1].AuthorName);
        Assert.Equal(_alice.CharacterId, ChatMessageAnnotations.GroupSpeakerOf(_group.History[1]));
        Assert.Equal(_bob.SessionId, ChatMessageAnnotations.GroupSpeakerSessionOf(_group.History[2]));
        Assert.False(_coordinator.IsRunning(_group.SessionId)); //一圈即停
    }

    [Fact]
    public async Task NextRound_SkipsOwnPostsAndWhatWasAlreadyDelivered()
    {
        await _coordinator.PostAsync(_group, "大家好");
        _runner.Calls.Clear();

        await _coordinator.RunRoundAsync(_group);

        // Alice 上一圈之后只多了 Bob 的话：用户那句已经交过，她自己的话本来就在她会话里
        string aliceInput = _runner.Calls[0].Input;
        Assert.Contains("[Bob]: Bob 的第 1 次发言", aliceInput);
        Assert.DoesNotContain("大家好", aliceInput);
        Assert.DoesNotContain("[Alice]", aliceInput);
        Assert.Equal("[Alice]: Alice 的第 2 次发言", _runner.Calls[1].Input);
    }

    [Fact]
    public async Task Scene_IsGivenOnlyOnTheFirstDelivery()
    {
        await _coordinator.PostAsync(_group, "大家好");
        Assert.Contains("群聊「会审」", _runner.Calls[0].Input);
        _runner.Calls.Clear();

        await _coordinator.RunRoundAsync(_group);

        Assert.DoesNotContain("群聊「会审」", _runner.Calls[0].Input);
    }

    [Fact]
    public async Task PostViaSendMessage_ReplacesTheFinalText()
    {
        _runner.During[_alice.SessionId] = () =>
        {
            Assert.True(_coordinator.TryPostFromMember(_alice.SessionId, "我先去翻一下代码"));
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");

        List<string> alicePosts = _group.History
            .Where(x => ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _alice.SessionId)
            .Select(x => x.Text)
            .ToList();
        Assert.Equal(["我先去翻一下代码"], alicePosts);
    }

    [Fact]
    public async Task UserInterjection_ReachesTheSpeakerOnceAndTheOthersAtTheirTurn()
    {
        _runner.During[_alice.SessionId] = () => _coordinator.PostAsync(_group, "插一句");

        await _coordinator.PostAsync(_group, "大家好");

        Assert.Single(_runner.Injected);
        Assert.Equal(_alice.SessionId, _runner.Injected[0].Member.SessionId);
        Assert.Contains("插一句", _runner.Calls[1].Input); //Bob 轮到时照常收到
        _runner.Calls.Clear();

        await _coordinator.RunRoundAsync(_group);

        Assert.DoesNotContain("插一句", _runner.Calls[0].Input); //插给过 Alice 的不再投一遍
    }

    [Fact]
    public async Task Stop_EndsTheRoundAndPostsNothingHalfDone()
    {
        _runner.During[_alice.SessionId] = () =>
        {
            _coordinator.Stop(_group.SessionId);
            return Task.CompletedTask;
        };
        _runner.Fail.Add(_alice.SessionId);

        await _coordinator.PostAsync(_group, "大家好");

        Assert.Single(_runner.Calls); //Bob 没轮到
        Assert.Single(_group.History); //只有用户那句
    }

    [Fact]
    public async Task FailedTurn_PostsNothingButTheRoundGoesOn()
    {
        _runner.Fail.Add(_alice.SessionId);

        await _coordinator.PostAsync(_group, "大家好");

        Assert.Equal(2, _runner.Calls.Count);
        Assert.DoesNotContain(_group.History, x => ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _alice.SessionId);
    }

    [Fact]
    public void TryPostFromMember_RefusesNonMembers()
    {
        ChatSession loner = Track(new ChatSession { IsTransient = true });

        Assert.False(_coordinator.TryPostFromMember(loner.SessionId, "hi"));
    }

    [Fact]
    public async Task SpeakerChanged_TracksWhoIsSpeakingAcrossTheRound()
    {
        List<string> events = [];
        _coordinator.SpeakerChanged += id => events.Add(id);
        _runner.During[_alice.SessionId] = () =>
        {
            Assert.Equal(_alice.SessionId, _coordinator.CurrentSpeakerOf(_group.SessionId));
            return Task.CompletedTask;
        };
        _runner.During[_bob.SessionId] = () =>
        {
            Assert.Equal(_bob.SessionId, _coordinator.CurrentSpeakerOf(_group.SessionId));
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");

        Assert.All(events, id => Assert.Equal(_group.SessionId, id));
        Assert.True(events.Count >= 4, $"轮到/结束各应通报一次,实际 {events.Count}");
        Assert.Null(_coordinator.CurrentSpeakerOf(_group.SessionId)); //一圈即停后清空
    }

    [Fact]
    public async Task SpeakerChanged_DoesNotFireWhenNobodyHasNewLines()
    {
        await _coordinator.PostAsync(_group, "大家好"); //第一圈两人都开口
        _runner.Silent = true; //此后成员不再产生群发言,log 不再增长
        await _coordinator.RunRoundAsync(_group); //第二圈把各人游标推到 log 尾部
        int fired = 0;
        _coordinator.SpeakerChanged += _ => fired++;

        await _coordinator.RunRoundAsync(_group); //第三圈全员没有新话,全部跳过

        Assert.Equal(0, fired);
    }

    [Theory]
    [InlineData("group", true)]
    [InlineData(" Group ", true)]
    [InlineData("群", true)]
    [InlineData("Alice", false)]
    [InlineData(null, false)]
    public void GroupAddress_IsRecognized(string? to, bool expected)
    {
        Assert.Equal(expected, GroupChatCoordinator.IsGroupAddress(to));
    }

    [Fact]
    public void Delivery_IsNullWhenNothingIsNew()
    {
        Assert.Null(GroupTranscript.BuildDelivery([], 0, "someone"));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)] //普通群不收智能体:它写的文件没处落
    [InlineData(true, true, true)]
    public void CanJoin_FollowsTheGroupType(bool isAgentGroup, bool isAgent, bool expected)
    {
        Assert.Equal(expected, GroupChatSessions.CanJoin(new CharacterData { IsAgent = isAgent }, isAgentGroup));
    }

    [Fact]
    public void GroupShell_SidesByGroupTypeNotByItsPlaceholderCharacter()
    {
        ChatSessionMeta agentGroup = new() { IsGroup = true, IsAgentGroup = true };
        ChatSessionMeta chatGroup = new() { IsGroup = true, IsAgentGroup = false };

        Assert.True(SessionManager.IsAgentSide(agentGroup));
        Assert.False(SessionManager.IsChatSide(agentGroup));
        Assert.True(SessionManager.IsChatSide(chatGroup));
        Assert.False(SessionManager.IsAgentSide(chatGroup));
    }

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

    /// <summary>假的成员一轮：把输入与一句编号的回答写进成员会话，就像真跑过一样</summary>
    private sealed class FakeRunner : IGroupMemberTurnRunner
    {
        private readonly Dictionary<string, int> _spoken = new();

        public List<(ChatSession Member, string Input)> Calls { get; } = [];
        public List<(ChatSession Member, string Text)> Injected { get; } = [];
        public Dictionary<string, Func<Task>> During { get; } = new();
        public HashSet<string> Fail { get; } = [];

        /// <summary>打开后本轮不再产生群发言（log 不再增长），用于构造「全员无新话」的静止场景</summary>
        public bool Silent { get; set; }

        public async Task<bool> RunAsync(ChatSession member, ChatMessage input, CancellationToken cancellationToken)
        {
            Calls.Add((member, input.Text));
            member.History.Add(input);
            if (During.TryGetValue(member.SessionId, out Func<Task>? during)) await during();
            if (Fail.Contains(member.SessionId)) return false;
            if (Silent) return true;

            int count = _spoken.GetValueOrDefault(member.SessionId) + 1;
            _spoken[member.SessionId] = count;
            member.History.Add(new ChatMessage(ChatRole.Assistant, $"{member.CharacterData.CharacterName} 的第 {count} 次发言"));
            return true;
        }

        public Task<bool> TryInjectAsync(ChatSession member, ChatMessage message)
        {
            Injected.Add((member, message.Text));
            return Task.FromResult(true);
        }
    }
}
