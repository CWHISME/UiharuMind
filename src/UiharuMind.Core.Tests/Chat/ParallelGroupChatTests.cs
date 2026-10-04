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

    /// <summary>群闲着时有附注：只叫他一个，别人有没听过的话也不跟着醒</summary>
    [Fact]
    public async Task NotifyMember_WhenIdle_WakesOnlyThatMember()
    {
        Assert.True(_coordinator.TryPostFromMember(_bob.SessionId, "我先说一句")); //Alice 与 Carol 都有新话没听

        Assert.True(await _coordinator.NotifyMemberAsync(_alice.SessionId, "后台任务跑完了"));

        Assert.Equal(1, _runner.CallsOf(_alice));
        Assert.Equal(0, _runner.CallsOf(_carol));
        Assert.StartsWith("后台任务跑完了", _runner.Calls.Single(x => x.Member == _alice).Input);
    }

    /// <summary>他正跑着时来了附注：这一轮说完再叫他一次，附注随那次投递交出</summary>
    [Fact]
    public async Task NotifyMember_WhileRunning_RewakesAfterThisTurn()
    {
        _runner.Silent = true; //别人不发言:第二次叫醒只能来自附注
        Task<bool>? notified = null;
        _runner.During[_alice.SessionId] = () =>
        {
            notified ??= _coordinator.NotifyMemberAsync(_alice.SessionId, "后台任务跑完了");
            return Task.CompletedTask;
        };

        await _coordinator.PostAsync(_group, "大家好");
        Assert.True(await notified!);

        Assert.Equal(2, _runner.CallsOf(_alice));
        Assert.StartsWith("后台任务跑完了", _runner.Calls.Where(x => x.Member == _alice).Last().Input);
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
    public async Task Host_IsTheOnlyOneWoken_AndIsNotWokenAgainAfterNaming()
    {
        _group.GroupHostSessionId = _carol.SessionId;
        _runner.Replies[_carol.SessionId] = _ => "@Alice 你先说说";

        await _coordinator.PostAsync(_group, "这个方案怎么样");

        Assert.Equal(1, _runner.CallsOf(_carol));
        Assert.Equal(1, _runner.CallsOf(_alice)); //被点名
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
        TaskCompletionSource aliceGate = new();
        _runner.During[_alice.SessionId] = () => aliceGate.Task;

        Task posting = _coordinator.PostAsync(_group, "大家好");

        Assert.Equal([_alice.SessionId], _coordinator.SpeakersOf(_group.SessionId)); //Bob、Carol 已说完，Alice 还在说
        // 广播在线程池上排队做，等它到了再看
        await WaitUntil(() => _runner.Injected.Count(x => x.Member == _alice) >= 2);
        Assert.Contains(_runner.Injected, x => x.Member == _alice && x.Text == GroupTranscript.FormatInjection("Bob", "Bob 的第 1 次发言"));
        Assert.Contains(_runner.Injected, x => x.Member == _alice && x.Text == GroupTranscript.FormatInjection("Carol", "Carol 的第 1 次发言"));
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

        Assert.Contains(_runner.Injected, x => x.Member == _alice && x.Text == GroupTranscript.FormatInjection("Bob", "@Alice 你怎么看"));
        Assert.Equal(1, _runner.CallsOf(_carol)); //Alice 被 Bob 点到之后的 @Carol 不再叫醒她
    }

    /// <summary>
    /// 跑着时被主持人点名：他接下来说的按主持人叫醒算（与被成员点到降成第二跳对称，按最近一跳）。
    /// Alice 先被 Bob 叫醒（第二跳），跑着时 Carol（主持人）又点了她，她说完 @Bob 就还叫得醒
    /// </summary>
    [Fact]
    public async Task Conservative_NamedByHostWhileSpeaking_HerNextWordsCountAsHostWoken()
    {
        _group.GroupHostSessionId = _carol.SessionId;
        TaskCompletionSource carolGate = new();
        TaskCompletionSource aliceGate = new();
        _runner.During[_carol.SessionId] = () =>
        {
            Assert.True(_coordinator.TryPostFromMember(_carol.SessionId, "@Bob 你先说"));
            return carolGate.Task;
        };
        _runner.During[_alice.SessionId] = () => aliceGate.Task;
        _runner.Replies[_bob.SessionId] = n => n == 1 ? "@Alice 你看看" : "补完了";
        _runner.Replies[_alice.SessionId] = _ => "@Bob 你再补一句";

        Task posting = _coordinator.PostAsync(_group, "这个方案怎么样");
        await WaitUntil(() => _runner.CallsOf(_alice) == 1);
        Assert.True(_coordinator.TryPostFromMember(_carol.SessionId, "@Alice 展开讲讲"));
        await WaitUntil(() => _runner.Injected.Any(x => x.Member == _alice && x.Text.Contains("展开讲讲")));
        aliceGate.SetResult();
        carolGate.SetResult();
        await posting;

        Assert.Equal(2, _runner.CallsOf(_bob));
    }

    /// <summary>
    /// 开跑后中途抛出（这里是订阅方）：这一轮也得摘掉。不摘的话他在这一波里一直算「在说」，
    /// 之后每条广播都插进一个已经没人消费的轮次，他下一轮开跑时连同新投递一起冒出来
    /// </summary>
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
    public async Task UnaddressedPost_WhileSheIsFinishing_DoesNotMakeHerSpeakAgain_AndIsDeliveredNextTime()
    {
        TaskCompletionSource aliceGate = new();
        _runner.Generating[_alice.SessionId] = () => aliceGate.Task;

        Task posting = _coordinator.PostAsync(_group, "大家好");
        await WaitUntil(() => _runner.Injected.Count(x => x.Member == _alice) >= 2);
        aliceGate.SetResult();
        await posting;

        Assert.Single(_alice.History, x => x.Role == ChatRole.Assistant);
        Assert.Equal(1, _runner.CallsOf(_alice));

        _runner.Generating.Clear();
        await _coordinator.ContinueAsync(_group);

        string next = _runner.Calls.Last(x => x.Member == _alice).Input;
        Assert.Contains("[Bob]: Bob 的第 1 次发言", next);
        Assert.Contains("[Carol]: Carol 的第 1 次发言", next);
    }

    /// <summary>封口撤回的那句点了她名：说完再叫她一次（与「插话没被消费」同一条补叫）</summary>
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
    public async Task Conservative_DoesNotCatchUp()
    {
        await _coordinator.PostAsync(_group, "大家好");

        Assert.DoesNotContain(_runner.Calls, x => x.Input.Contains(GroupTranscript.CatchUpHint));
    }

    /// <summary>这一波没跑成的（这里是失败）不被补位自动重跑：失败多半还会再失败，交给「继续」</summary>
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
