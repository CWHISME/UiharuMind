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

    /// <summary>
    /// 群轮被停或失败的成员：投递已交给过他、游标早推过去，没新话也要能被「继续」叫起来接着做；做完就不再叫
    /// </summary>
    [Fact]
    public async Task InterruptedMember_ResumesOnContinue_EvenWithNothingNew()
    {
        _runner.Fail.Add(_alice.SessionId);
        _runner.Silent = true; //别人不发言：她醒来时没有新话可接
        await _coordinator.PostAsync(_group, "大家好"); //没主持人：全员都叫醒、都听过了
        _runner.Fail.Clear();
        _runner.ClearCalls();

        await _coordinator.ContinueAsync(_group);

        (ChatSession member, string input) = Assert.Single(_runner.Calls);
        Assert.Same(_alice, member);
        Assert.StartsWith(GroupTranscript.ResumeNote, input);

        _runner.ClearCalls();
        await _coordinator.ContinueAsync(_group);
        Assert.Empty(_runner.Calls);
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
        _group.GroupStopPolicy = EGroupStopPolicy.Aggressive;
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
    /// 保守档：没点到她的闲聊照样实时插进她正在跑的这一轮——她得看得到同伴说了什么；
    /// 没被叫醒的人不会因此开口（叫醒仍按唤醒边界）
    /// </summary>
    [Fact]
    public async Task Conservative_UnaddressedChatter_StillReachesTheRunningMember()
    {
        TaskCompletionSource aliceGate = new();
        _runner.During[_alice.SessionId] = () => aliceGate.Task;

        Task posting = _coordinator.PostAsync(_group, "大家好");
        // 两句都插到了再放她说完：只等 Bob 那句，Carol 的可能落在她说完之后，那就不是插话了
        await WaitUntil(() => _runner.Injected.Count(x => x.Member == _alice) >= 2);
        aliceGate.SetResult();
        await posting;

        Assert.Contains(_runner.Injected, x => x.Member == _alice && x.Text.Contains("[Bob]:"));
        Assert.Contains(_runner.Injected, x => x.Member == _alice && x.Text.Contains("[Carol]:"));
        Assert.Equal(1, _runner.CallsOf(_bob)); //闲聊不叫醒人：Bob、Carol 各说一次就停
        Assert.Equal(1, _runner.CallsOf(_carol));
    }

    /// <summary>保守档：她跑着时被成员点到，接下来说的话按第二跳算，不再叫醒别人</summary>
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

        Assert.Contains(_runner.Injected, x => x.Member == _alice && x.Text == "[Bob]: @Alice 你怎么看");
        Assert.Equal(1, _runner.CallsOf(_carol)); //Alice 被 Bob 点到之后的 @Carol 不再叫醒她
    }

    [Fact]
    public async Task PassReply_IsNotPostedToTheGroup()
    {
        _runner.Replies[_bob.SessionId] = _ => "[跳过]";

        await _coordinator.PostAsync(_group, "大家好");

        Assert.DoesNotContain(_group.History, x => ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _bob.SessionId);
        Assert.Contains(_group.History, x => ChatMessageAnnotations.GroupSpeakerSessionOf(x) == _alice.SessionId);
        Assert.DoesNotContain(_bob.History, ChatMessageAnnotations.IsPostedToGroup);
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
