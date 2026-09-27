using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 并行群聊（ADR 0049）：唤醒边界、主持人点名、实时广播、一跳防护。
/// 跑模型的那一段换成假的；会话一律是临时的，不落盘
/// </summary>
public class ParallelGroupChatTests
{
    private readonly Dictionary<string, ChatSession> _sessions = new();
    private readonly FakeGroupMemberTurnRunner _runner = new();
    private readonly GroupChatCoordinator _coordinator;
    private readonly ChatSession _group;
    private readonly ChatSession _alice;
    private readonly ChatSession _bob;
    private readonly ChatSession _carol;

    public ParallelGroupChatTests()
    {
        DefaultCharacterManager.Instance.OnInitialize(); //用户发言的署名取自内置用户卡
        _coordinator = new GroupChatCoordinator(_runner, id => _sessions.GetValueOrDefault(id));
        _group = Track(new ChatSession
        {
            Title = "会审", IsGroup = true, IsAgentGroup = true, IsTransient = true,
            GroupScheduleMode = EGroupScheduleMode.Parallel,
        });
        _alice = Member("Alice");
        _bob = Member("Bob");
        _carol = Member("Carol");
        _group.GroupMemberSessionIds = [_alice.SessionId, _bob.SessionId, _carol.SessionId];
    }

    [Fact]
    public async Task UserPost_WithoutHost_WakesEveryone_AndMemberPostsWakeNobody()
    {
        await _coordinator.PostAsync(_group, "大家好");

        Assert.Equal(1, _runner.CallsOf(_alice));
        Assert.Equal(1, _runner.CallsOf(_bob));
        Assert.Equal(1, _runner.CallsOf(_carol));
        Assert.False(_coordinator.IsRunning(_group.SessionId));
    }

    [Fact]
    public async Task UserMention_WakesOnlyTheMentioned()
    {
        await _coordinator.PostAsync(_group, "@Bob 你看看");

        Assert.Equal(0, _runner.CallsOf(_alice));
        Assert.Equal(1, _runner.CallsOf(_bob));
        Assert.Equal(0, _runner.CallsOf(_carol));
    }

    [Fact]
    public async Task Host_IsTheOnlyOneWoken_GetsTheHint_AndIsNotWokenAgainAfterNaming()
    {
        _group.GroupHostSessionId = _carol.SessionId;
        _runner.Replies[_carol.SessionId] = _ => "@Alice 你先说说";

        await _coordinator.PostAsync(_group, "这个方案怎么样");

        Assert.Equal(1, _runner.CallsOf(_carol));
        Assert.Contains(GroupTranscript.HostColdStartHint, _runner.Calls[0].Input);
        Assert.Contains("你是本群主持人", _runner.Calls[0].Input);
        Assert.Equal(1, _runner.CallsOf(_alice)); //被点名
        Assert.DoesNotContain(GroupTranscript.HostColdStartHint, _runner.Calls[1].Input);
        Assert.Equal(0, _runner.CallsOf(_bob));
    }

    [Fact]
    public async Task Conservative_NamedByHostCanWakeOthers_ButOnlyOneHop()
    {
        _group.GroupHostSessionId = _carol.SessionId;
        _runner.Replies[_carol.SessionId] = _ => "@Alice 你先说说";
        _runner.Replies[_alice.SessionId] = _ => "@Bob 你补充一下";
        _runner.Replies[_bob.SessionId] = _ => "@Alice @Carol 补充完了";

        await _coordinator.PostAsync(_group, "这个方案怎么样");

        Assert.Equal(1, _runner.CallsOf(_carol)); //Bob 的 @ 是成员叫醒的人发的：不再叫醒任何人
        Assert.Equal(1, _runner.CallsOf(_alice));
        Assert.Equal(1, _runner.CallsOf(_bob));
    }

    [Fact]
    public async Task SmallGroup_Conservative_IsOneQuestionOneAnswer()
    {
        _group.GroupMemberSessionIds = [_alice.SessionId, _bob.SessionId];

        await _coordinator.PostAsync(_group, "hi");

        Assert.Equal(1, _runner.CallsOf(_alice));
        Assert.Equal(1, _runner.CallsOf(_bob));
    }

    [Fact]
    public async Task SmallGroup_Aggressive_KeepsGoingUntilStopped()
    {
        _group.GroupMemberSessionIds = [_alice.SessionId, _bob.SessionId];
        _group.GroupStopPolicy = EGroupStopPolicy.Aggressive;
        _runner.During[_alice.SessionId] = () =>
        {
            if (_runner.CallsOf(_alice) >= 3) _coordinator.Stop(_group.SessionId);
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "hi");

        Assert.Equal(3, _runner.CallsOf(_alice));
        Assert.True(_runner.CallsOf(_bob) >= 2);
        Assert.False(_coordinator.IsRunning(_group.SessionId));
    }

    [Fact]
    public async Task Broadcast_ReachesMembersStillSpeaking_AndIsNotDeliveredAgain()
    {
        _group.GroupStopPolicy = EGroupStopPolicy.Aggressive; //激进档：闲聊也实时插给在跑的人
        TaskCompletionSource aliceGate = new();
        _runner.During[_alice.SessionId] = () => aliceGate.Task;

        Task posting = _coordinator.PostAsync(_group, "大家好");

        Assert.Equal([_alice.SessionId], _coordinator.SpeakersOf(_group.SessionId)); //Bob、Carol 已说完，Alice 还在说
        // 广播在线程池上排队做，等它到了再看
        await WaitUntil(() => _runner.Injected.Count(x => x.Member == _alice) >= 2);
        Assert.Contains(_runner.Injected, x => x.Member == _alice && x.Text == "[Bob]: Bob 的第 1 次发言");
        Assert.Contains(_runner.Injected, x => x.Member == _alice && x.Text == "[Carol]: Carol 的第 1 次发言");
        Assert.DoesNotContain(_runner.Injected, x => x.Member == _carol); //Bob 说时 Carol 还没开口：随投递拿到，不插
        Assert.Contains("[Bob]: Bob 的第 1 次发言", _runner.Calls.Single(x => x.Member == _carol).Input);

        aliceGate.SetResult();
        await posting;
        _runner.During.Clear();
        await _coordinator.ContinueAsync(_group);

        Assert.Equal(1, _runner.CallsOf(_alice)); //插给过她的都已消费，没有新话，不叫
    }

    /// <summary>
    /// 保守档：没点到她的闲聊不插进她正在跑的这一轮（插进去她就会多回一句，大家互相续下去），
    /// 等她下一轮随投递看到
    /// </summary>
    [Fact]
    public async Task Conservative_UnaddressedChatter_IsNotInjected_ButDeliveredLater()
    {
        TaskCompletionSource aliceGate = new();
        _runner.During[_alice.SessionId] = () => aliceGate.Task;

        Task posting = _coordinator.PostAsync(_group, "大家好");
        // Bob、Carol 说完，再给广播队列一点时间跑完——要验的是「没插」，只能等一会儿再看
        await WaitUntil(() => _group.History.Count >= 3);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(_runner.Injected, x => x.Member == _alice);
        aliceGate.SetResult();
        await posting;
        _runner.During.Clear();

        await _coordinator.ContinueAsync(_group);
        Assert.Contains("[Bob]: Bob 的第 1 次发言", _runner.Calls.Last(x => x.Member == _alice).Input);
    }

    /// <summary>保守档：点到了她才插；她被成员点到之后再说的话按第二跳算，不再叫醒别人</summary>
    [Fact]
    public async Task Conservative_MentionReachesTheRunningMember_AndHerNextWordsAreTheSecondHop()
    {
        TaskCompletionSource aliceGate = new();
        _runner.During[_alice.SessionId] = () => aliceGate.Task;
        _runner.Replies[_bob.SessionId] = _ => "@Alice 你怎么看";
        _runner.Replies[_alice.SessionId] = _ => "@Carol 你来补充";

        Task posting = _coordinator.PostAsync(_group, "大家好");
        await WaitUntil(() => _runner.Injected.Any(x => x.Member == _alice));
        aliceGate.SetResult();
        await posting;

        Assert.Equal(["[Bob]: @Alice 你怎么看"], _runner.Injected.Where(x => x.Member == _alice).Select(x => x.Text));
        Assert.Equal(1, _runner.CallsOf(_carol)); //Alice 被 Bob 点到之后的 @Carol 不再叫醒她
    }

    [Fact]
    public async Task PassReply_IsNotPostedToTheGroup()
    {
        _runner.Replies[_bob.SessionId] = _ => "[跳过]";

        await _coordinator.PostAsync(_group, "大家好");

        Assert.DoesNotContain(_group.History, x => ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _bob.SessionId);
        Assert.Contains(_group.History, x => ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _alice.SessionId);
    }

    [Fact]
    public async Task SeveralMembersSpeakAtOnce()
    {
        TaskCompletionSource gate = new();
        _runner.During[_alice.SessionId] = () => gate.Task;
        _runner.During[_bob.SessionId] = () => gate.Task;

        Task posting = _coordinator.PostAsync(_group, "大家好");

        Assert.Equal([_alice.SessionId, _bob.SessionId], _coordinator.SpeakersOf(_group.SessionId));
        gate.SetResult();
        await posting;
        Assert.Empty(_coordinator.SpeakersOf(_group.SessionId));
    }

    [Fact]
    public async Task MentionedWhileSpeaking_UnconsumedInterjection_WakesHimAgain()
    {
        _runner.InjectionsNeverConsumed = true;
        TaskCompletionSource aliceGate = new();
        _runner.During[_alice.SessionId] = () => aliceGate.Task;

        Task posting = _coordinator.PostAsync(_group, "@Alice 你来");
        await _coordinator.PostAsync(_group, "@Alice 再补一句");
        _runner.During.Clear();
        aliceGate.SetResult();
        await posting;

        Assert.Equal(2, _runner.CallsOf(_alice)); //那句没被消费：说完再叫她一次
        Assert.Contains("再补一句", _runner.Calls.Last(x => x.Member == _alice).Input);
    }

    [Fact]
    public async Task MentionedWhileSpeaking_ConsumedInterjection_CountsAsHisResponse()
    {
        TaskCompletionSource aliceGate = new();
        _runner.During[_alice.SessionId] = () => aliceGate.Task;

        Task posting = _coordinator.PostAsync(_group, "@Alice 你来");
        await _coordinator.PostAsync(_group, "@Alice 再补一句");
        aliceGate.SetResult();
        await posting;

        Assert.Equal(1, _runner.CallsOf(_alice));
    }

    [Fact]
    public async Task Stop_EndsEveryoneStillSpeaking()
    {
        TaskCompletionSource gate = new();
        _runner.During[_alice.SessionId] = () => gate.Task;
        _runner.During[_bob.SessionId] = () => gate.Task;

        Task posting = _coordinator.PostAsync(_group, "大家好");
        _coordinator.Stop(_group.SessionId);
        gate.SetResult();
        await posting;

        Assert.DoesNotContain(_group.History, x => ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _alice.SessionId);
        Assert.DoesNotContain(_group.History, x => ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _bob.SessionId);
        Assert.False(_coordinator.IsRunning(_group.SessionId));
    }

    [Fact]
    public async Task ModeSwitch_TakesEffectFromTheNextRun()
    {
        TaskCompletionSource aliceGate = new();
        _runner.During[_alice.SessionId] = () => aliceGate.Task;

        Task posting = _coordinator.PostAsync(_group, "大家好");
        _group.GroupScheduleMode = EGroupScheduleMode.Serial;
        await _coordinator.PostAsync(_group, "@Bob 你再看看"); //这一波仍是并行：@ 照样叫醒 Bob
        aliceGate.SetResult();
        await posting;

        Assert.Equal(2, _runner.CallsOf(_bob));
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

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "等了 2 秒还没等到");
    }
}
