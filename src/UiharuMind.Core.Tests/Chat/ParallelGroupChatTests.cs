using Microsoft.Extensions.AI;
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
    private readonly List<(TimeSpan Delay, TaskCompletionSource Due)> _delays = [];
    private readonly GroupChatCoordinator _coordinator;
    private readonly ChatSession _group;
    private readonly ChatSession _alice;
    private readonly ChatSession _bob;
    private readonly ChatSession _carol;

    public ParallelGroupChatTests() 
    {
        DefaultCharacterManager.Instance.OnInitialize(); //用户发言的署名取自内置用户卡
        _coordinator = new GroupChatCoordinator(_runner, id => _sessions.GetValueOrDefault(id), delay: Delay);
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

    /// <summary>
    /// 用户私聊正在群里说话的成员（ADR 0063）：只叫停他这一轮，别人照常说；
    /// 私聊完等 30 秒（让用户看完私聊回复）再单独叫醒他，期间群里的新话照常投，
    /// 末尾先钉死通道（上一轮是私聊回复、没进群）再附上被私聊打断的交代
    /// </summary>
    [Fact]
    public async Task PrivateChat_ResumesAfterThirtySeconds_RetimesOnFollowUpPrivate()
    {
        Task<IDisposable>? privateTurn = null;
        _runner.During[_alice.SessionId] = () =>
        {
            privateTurn ??= GroupMemberTurnGate.EnterPrivateAsync(_alice.SessionId);
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");
        IDisposable lease = await privateTurn!;
        lease.Dispose();

        await WaitUntil(() => _delays.Count == 1);
        Assert.Equal(TimeSpan.FromSeconds(30), _delays[0].Delay);

        // 等的期间又私聊了一句：成员历史变长，到点时重排、不叫醒
        _alice.History.Add(new ChatMessage(ChatRole.User, "追一句"));
        _delays[0].Due.SetResult();
        await WaitUntil(() => _delays.Count == 2);
        Assert.Equal(TimeSpan.FromSeconds(30), _delays[1].Delay);
        Assert.Equal(1, _runner.CallsOf(_alice));

        // 这一轮没再私聊：到点叫醒，交代还在
        _delays[1].Due.SetResult();
        await WaitUntil(() => _runner.CallsOf(_alice) == 2 && !_coordinator.IsRunning(_group.SessionId));
        Assert.EndsWith(GroupTranscript.PrivateResumeNote, _runner.Calls.Last(x => x.Member == _alice).Input);
    }

    /// <summary>装配阶段就被私聊叫停：投递没进他的历史，接回时那几条照样交给他，不因游标已推过去而丢</summary>
    [Fact]
    public async Task PrivateChat_PreemptedWhileAttaching_RedeliversThePosts()
    {
        Task<IDisposable>? privateTurn = null;
        _runner.Attaching[_alice.SessionId] = () =>
        {
            privateTurn ??= GroupMemberTurnGate.EnterPrivateAsync(_alice.SessionId);
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");
        IDisposable lease = await privateTurn!;
        Assert.DoesNotContain(_alice.History, x => x.Text.Contains("大家好"));

        lease.Dispose();
        await WaitUntil(() => _delays.Count == 1); //私聊完等 30 秒再接回
        _delays[0].Due.SetResult();
        await WaitUntil(() => _runner.CallsOf(_alice) == 2 && !_coordinator.IsRunning(_group.SessionId));
        Assert.Contains("大家好", _runner.Calls.Last(x => x.Member == _alice).Input);
        Assert.Contains(_alice.History, x => x.Text.Contains("大家好"));
    }

    /// <summary>私聊期间他被移出群：私聊完不再叫醒他</summary>
    [Fact]
    public async Task PrivateChat_RemovedFromGroupMeanwhile_IsNotResumed()
    {
        Task<IDisposable>? privateTurn = null;
        _runner.During[_alice.SessionId] = () =>
        {
            privateTurn ??= GroupMemberTurnGate.EnterPrivateAsync(_alice.SessionId);
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");
        IDisposable lease = await privateTurn!;
        _group.GroupMemberSessionIds = [_bob.SessionId, _carol.SessionId];
        lease.Dispose();
        await WaitUntil(() => _delays.Count == 1); //接回排上 30 秒，但退群了，到点也不叫
        _delays[0].Due.SetResult();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(1, _runner.CallsOf(_alice));
    }

    /// <summary>群闲着时有附注：只叫他一个，别人有没听过的话也不跟着醒</summary>
    [Fact]
    public async Task NotifyMember_WhileRunning_InjectsIntoThisTurn()
    {
        _runner.Silent = true; //别人不发言:要是再叫醒只能来自附注
        Task<bool>? notified = null;
        _runner.During[_alice.SessionId] = () =>
        {
            notified ??= _coordinator.NotifyMemberAsync(_alice.SessionId, new ChatMessage(ChatRole.User, "后台任务跑完了"));
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");
        Assert.True(await notified!);

        Assert.Equal(1, _runner.CallsOf(_alice));
        Assert.Contains(_runner.Injected, x => x.Member == _alice && x.Text == "后台任务跑完了");
    }

    /// <summary>插进去了却没被消费（他已在最后那次调用里，说完被撤回）：这一轮说完再叫他一次，附注随那次投递交出</summary>
    [Fact]
    public async Task NotifyMember_InjectedButWithdrawn_RewakesAfterThisTurn()
    {
        _runner.Silent = true;
        _runner.InjectionsNeverConsumed = true;
        Task<bool>? notified = null;
        _runner.During[_alice.SessionId] = () =>
        {
            notified ??= _coordinator.NotifyMemberAsync(_alice.SessionId, new ChatMessage(ChatRole.User, "后台任务跑完了"));
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");
        Assert.True(await notified!);

        Assert.Equal(2, _runner.CallsOf(_alice));
        Assert.StartsWith("后台任务跑完了", _runner.Calls.Where(x => x.Member == _alice).Last().Input);
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
    public async Task TurnThatThrowsAfterBegin_IsEnded_AndGetsNoMoreBroadcasts()
    {
        _group.GroupMemberSessionIds = [_alice.SessionId, _bob.SessionId, _carol.SessionId];
        bool thrown = false;
        _coordinator.SpeakerChanged += _ =>
        {
            if (thrown) return;
            thrown = true;
            throw new InvalidOperationException("boom");
        };

        await _coordinator.PostAsync(_group, "大家好");

        Assert.Equal(0, _runner.CallsOf(_alice)); //第一个开口的是她，通报时就抛了
        Assert.DoesNotContain(_runner.Injected, x => x.Member == _alice);
    }

    [Fact]
    public async Task PassReply_IsNotPostedToTheGroup()
    {
        _runner.Replies[_bob.SessionId] = _ => GroupTranscript.PassReply;

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

    /// <summary>
    /// 说完即封口（ADR 0049 修订）：别人在她说最后一句时发言，不会让她为这句没点她名的话再说一次；
    /// 那句撤回来，下次叫醒她时随投递拿到
    /// </summary>
    [Fact]
    public async Task MentionWhileSheIsFinishing_IsWithdrawn_AndWakesHerAgain()
    {
        TaskCompletionSource aliceGate = new();
        _runner.Generating[_alice.SessionId] = () => aliceGate.Task;
        _runner.Replies[_bob.SessionId] = _ => "@Alice 你怎么看";

        Task posting = _coordinator.PostAsync(_group, "大家好");
        await WaitUntil(() => _runner.Injected.Any(x => x.Member == _alice && x.Text.Contains("@Alice")));
        _runner.Generating.Clear();
        aliceGate.SetResult();
        await posting;

        Assert.Equal(2, _runner.CallsOf(_alice));
        Assert.Contains("[Bob]: @Alice 你怎么看", _runner.Calls.Last(x => x.Member == _alice).Input);
    }

    /// <summary>串行不封口：插话按框架原样续轮（串行本来人人轮到，封口只为治并行里的续说）</summary>
    [Fact]
    public async Task Serial_DoesNotSeal()
    {
        _group.GroupScheduleMode = EGroupScheduleMode.Serial;
        TaskCompletionSource aliceGate = new();
        _runner.Generating[_alice.SessionId] = () => aliceGate.Task;

        Task posting = _coordinator.PostAsync(_group, "大家好");
        await WaitUntil(() => _coordinator.SpeakersOf(_group.SessionId).Contains(_alice.SessionId));
        await _coordinator.PostAsync(_group, "补一句");
        await WaitUntil(() => _runner.Injected.Any(x => x.Member == _alice));
        aliceGate.SetResult();
        await posting;

        Assert.Equal(2, _alice.History.Count(x => x.Role == ChatRole.Assistant));
    }

    /// <summary>
    /// 激进档的补位轮（ADR 0049 修订）：静下来后，还有没看过的发言的人各补一次，投递末尾带补位提示；
    /// 补位里说的话让别人又有了新话，也不再补第二轮
    /// </summary>
    [Fact]
    public async Task Aggressive_WhenQuiet_EveryoneWithUnreadCatchesUpOnce()
    {
        _group.GroupStopPolicy = EGroupStopPolicy.Aggressive;

        await _coordinator.PostAsync(_group, "大家好");

        List<(ChatSession Member, string Input)> catchUps =
            _runner.Calls.Where(x => x.Input.Contains(GroupTranscript.CatchUpHint)).ToList();
        Assert.NotEmpty(catchUps);
        Assert.All(catchUps, x => Assert.Equal(2, _runner.CallsOf(x.Member)));
        Assert.All(new[] { _alice, _bob, _carol }, x => Assert.True(_runner.CallsOf(x) <= 2));
        Assert.False(_coordinator.IsRunning(_group.SessionId));
    }

    [Fact]
    public async Task Aggressive_CatchUp_SkipsMembersWhoFailed()
    {
        _group.GroupStopPolicy = EGroupStopPolicy.Aggressive;
        _runner.Fail.Add(_alice.SessionId);

        await _coordinator.PostAsync(_group, "大家好");

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

    private Task Delay(TimeSpan delay)
    {
        TaskCompletionSource due = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_delays) _delays.Add((delay, due));
        return due.Task;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "等了 2 秒还没等到");
    }
}
