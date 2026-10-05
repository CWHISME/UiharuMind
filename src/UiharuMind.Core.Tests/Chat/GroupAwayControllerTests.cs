using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 离席（ADR 0055）：波末叫醒化身；没进展退避、用户停止后固定延迟、三道保险丝、手动结束。
/// 跑模型与等时间都换成假的：延迟只记下来，由测试决定什么时候到点
/// </summary>
public class GroupAwayControllerTests
{
    private static readonly GroupAwaySettings Settings = new(TimeSpan.FromHours(1), 10, 5,
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(5));

    private readonly Dictionary<string, ChatSession> _sessions = new();
    private readonly FakeGroupMemberTurnRunner _runner = new();
    private readonly List<(TimeSpan Delay, TaskCompletionSource Due)> _delays = [];
    private readonly List<GroupAwayReceipt> _receipts = [];
    private readonly GroupChatCoordinator _coordinator;
    private readonly GroupAwayController _away;
    private readonly ChatSession _group;
    private readonly ChatSession _alice;
    private readonly ChatSession _avatar;
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    private string _stamp = "0";
    private GroupAwaySettings _settings = Settings;

    public GroupAwayControllerTests()
    {
        DefaultCharacterManager.Instance.OnInitialize();
        _coordinator = new GroupChatCoordinator(_runner, id => _sessions.GetValueOrDefault(id));
        _group = Track(new ChatSession
        {
            Title = "会审", IsGroup = true, IsAgentGroup = true, IsTransient = true,
            GroupScheduleMode = EGroupScheduleMode.Parallel,
        });
        _alice = Member("Alice");
        _group.GroupMemberSessionIds = [_alice.SessionId, Member("Bob").SessionId, Member("Carol").SessionId];
        _avatar = Track(new ChatSession
        {
            CharacterId = nameof(DefaultCharacter.GroupAvatarAgent), GroupId = _group.SessionId,
            IsGroupAvatar = true, IsAgentForm = true, IsTransient = true,
        });
        _away = new GroupAwayController(_coordinator, (_, _) => _avatar, () => _settings, Delay, () => _now,
            _ => _stamp);
        _away.Ended += receipt =>
        {
            lock (_receipts) _receipts.Add(receipt);
        };
        // 每一波都落一件新产物，除非测试另说：保险丝「连续没有新产物」不来捣乱
        _coordinator.EpisodeEnded += _ => _stamp = Guid.NewGuid().ToString();
    }

    [Fact]
    public async Task FirstTurnFailed_KickoffRetriesWithTheGoal_NotDropped()
    {
        _runner.Fail.Add(_avatar.SessionId); //首轮没跑成（走失败）
        _runner.Replies[_avatar.SessionId] = _ => "@Alice 接着做";

        _away.Start(_group, "把两步做完", null);
        // 首轮失败：退避重试，捎话待重带
        await Until(() => _delays.Count == 1);
        Assert.Contains("把两步做完", LastAvatarInput());

        _runner.Fail.Clear();
        _delays[0].Due.SetResult();
        // 重试那一轮带上了捎话。不数精确轮次：化身每 Push 一次就开一波，波末又立即叫醒，
        // 连锁会一直跑到保险丝，AvatarCalls 从 1 直接跳着涨，== 2 是竞态
        await Until(() => _runner.Calls.Count(x => x.Member == _avatar && x.Input.Contains("把两步做完")) >= 2);

        // 捎话没丢：重试那一轮又带上了；之后再跑的不再带（对比跑成后的第二轮不再带）
        List<string> avatarInputs = _runner.Calls.Where(x => x.Member == _avatar).Select(x => x.Input).ToList();
        Assert.True(avatarInputs.Count >= 2);
        Assert.Contains("把两步做完", avatarInputs[0]); //首轮（失败那轮也带了）
        Assert.Contains("把两步做完", avatarInputs[1]); //重试轮重带
        Assert.All(avatarInputs.Skip(2), x => Assert.DoesNotContain("把两步做完", x));
    }

    [Fact]
    public async Task StartWhileGroupRuns_FirstTurnWaitsForTheWaveEnd_WithTheGoal()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _runner.During[_alice.SessionId] = () => gate.Task; //成员波卡在 Alice：群在跑
        _runner.Replies[_avatar.SessionId] = _ => "@Alice 接着做";

        Task wave = _coordinator.PostAsync(_group, "大家好");
        await Until(() => _coordinator.IsRunning(_group.SessionId));

        Assert.True(_away.Start(_group, "把两步做完", null));
        Assert.Equal(0, AvatarCalls()); //首轮不插进跑到一半的波

        gate.SetResult();
        await wave;
        // 波末那次首轮带捎话；之后又连锁跑到保险丝，不数精确轮次
        await Until(() => _runner.Calls.Any(x => x.Member == _avatar && x.Input.Contains("把两步做完")));
        Assert.Contains("把两步做完", _runner.Calls.First(x => x.Member == _avatar).Input); //首轮带捎话
    }

    [Fact]
    public async Task Busy_NotCountedAsATurn_AndCarriesTheNoteOver()
    {
        _runner.Replies[_avatar.SessionId] = _ => ""; //没写正文
        _away.Start(_group, "目标", null);
        await Until(() => AvatarCalls() == 1); //首轮跑成 Silent
        await Until(() => _delays.Count == 1); //SilentNote 排上退避

        using (GroupMemberTurnGate.TryEnter(_avatar.SessionId)) //用户正私聊化身：下一轮 Busy
        {
            _delays[0].Due.SetResult();
            await Until(() => _delays.Count == 2); //Busy 又排退避
            Assert.Equal(1, _away.StatusOf(_group.SessionId)?.AvatarTurns); //Busy 不计出手次数
        }

        _delays[1].Due.SetResult();
        await Until(() => AvatarCalls() == 2);
        Assert.EndsWith(GroupAvatarTranscript.SilentNote, LastAvatarInput()); //没交出去的提示带回来了
    }

    /// <summary>化身跑着时用户私聊它（ADR 0063）：叫停这一轮、不计出手次数，私聊完立即接回并附上交代</summary>
    [Fact]
    public async Task PrivateChat_PreemptsAvatarTurn_ResumesAfterwardsWithNote()
    {
        Task<IDisposable>? privateTurn = null;
        _runner.Replies[_avatar.SessionId] = _ => ""; //接回那轮没写正文：停在退避上，不连锁
        _runner.During[_avatar.SessionId] = () =>
        {
            if (AvatarCalls() == 1) privateTurn ??= GroupMemberTurnGate.EnterPrivateAsync(_avatar.SessionId);
            return Task.CompletedTask;
        };

        _away.Start(_group, "目标", null);
        await Until(() => privateTurn != null);
        IDisposable lease = await privateTurn!;
        await Until(() => _away.StatusOf(_group.SessionId)?.IsAvatarRunning == false);

        Assert.Equal(0, _away.StatusOf(_group.SessionId)?.AvatarTurns); //被叫停的不计出手次数
        Assert.Empty(_delays); //不排退避：私聊完就接回

        lease.Dispose();
        await Until(() => AvatarCalls() == 2);
        Assert.EndsWith(GroupTranscript.PrivateResumeNote, LastAvatarInput());
    }

    /// <summary>化身被私聊叫停、私聊期间用户停了群：私聊完不直接接回，按停下过一阵再叫</summary>
    [Fact]
    public async Task PrivateChat_GroupStoppedMeanwhile_AvatarWaitsTheStopDelay()
    {
        Task<IDisposable>? privateTurn = null;
        _runner.Replies[_avatar.SessionId] = _ => "";
        _runner.During[_avatar.SessionId] = () =>
        {
            if (AvatarCalls() == 1) privateTurn ??= GroupMemberTurnGate.EnterPrivateAsync(_avatar.SessionId);
            return Task.CompletedTask;
        };

        _away.Start(_group, "目标", null);
        await Until(() => privateTurn != null);
        IDisposable lease = await privateTurn!;
        await Until(() => _away.StatusOf(_group.SessionId)?.IsAvatarRunning == false);

        _coordinator.Stop(_group.SessionId);
        lease.Dispose();
        await Until(() => _delays.Count == 1);

        Assert.Equal(Settings.StopDelay, _delays[0].Delay);
        Assert.Equal(1, AvatarCalls());
    }

    [Fact]
    public async Task KickoffGoesToTheFirstAvatarTurnOnly_RestRideEveryTurn()
    {
        _runner.Replies[_avatar.SessionId] = _ => "@Alice 接着把第二步做完";
        _runner.During[_avatar.SessionId] = () =>
        {
            if (AvatarCalls() == 2) AddEndAway("done", "两步都做完了");
            return Task.CompletedTask;
        };

        Assert.True(_away.Start(_group, "把两步做完", null, "别动文档目录"));
        GroupAwayReceipt receipt = await NextReceipt();

        Assert.Equal(EGroupAwayEndReason.Done, receipt.Reason);
        Assert.Equal("两步都做完了", receipt.Summary);
        Assert.Equal(2, receipt.AvatarTurns);
        int pushed = Assert.Single(receipt.AvatarPostIndices);
        Assert.Equal("@Alice 接着把第二步做完", _group.History[pushed].Text);
        Assert.False(_away.IsAway(_group.SessionId));

        // 捎话与提醒都不进群；捎话只在首轮，提醒每轮都带
        Assert.DoesNotContain(_group.History, x => x.Text.Contains("把两步做完") || x.Text.Contains("别动文档目录"));
        List<string> avatarInputs = _runner.Calls.Where(x => x.Member == _avatar).Select(x => x.Input).ToList();
        Assert.Equal(2, avatarInputs.Count);
        Assert.Contains("把两步做完", avatarInputs[0]);
        Assert.DoesNotContain("把两步做完", avatarInputs[1]);
        Assert.All(avatarInputs, x => Assert.Contains("别动文档目录", x));

        // 回执落进群流水，只给人看：读得回来，也不会投递给任何人
        await Until(() => _group.History.Any(x => ChatMessageAnnotations.GroupAwayReceiptOf(x) != null));
        ChatMessage note = _group.History.Last(x => ChatMessageAnnotations.GroupAwayReceiptOf(x) != null);
        Assert.Equal(receipt, GroupAwayReceipt.FromJson(ChatMessageAnnotations.GroupAwayReceiptOf(note)!) with
        {
            AvatarPostIndices = receipt.AvatarPostIndices, Approvals = receipt.Approvals, AvatarActions = receipt.AvatarActions,
        });
        Assert.DoesNotContain("两步都做完了",
            GroupTranscript.BuildDelivery(_group.History, _group.History.IndexOf(note), _alice.SessionId) ?? "");
    }

    [Fact]
    public async Task Silence_BacksOffDoubling_AndTellsTheAvatarNextTime()
    {
        _runner.Replies[_avatar.SessionId] = _ => ""; //没写正文

        _away.Start(_group, "目标", null);
        await Until(() => _delays.Count == 1);
        Assert.Equal(TimeSpan.FromMinutes(1), _delays[0].Delay);
        Assert.Equal(_now + TimeSpan.FromMinutes(1), _away.StatusOf(_group.SessionId)?.WakeAt);

        _delays[0].Due.SetResult();
        await Until(() => _delays.Count == 2);
        Assert.EndsWith(GroupAvatarTranscript.SilentNote, LastAvatarInput());
        Assert.Equal(TimeSpan.FromMinutes(2), _delays[1].Delay);

        _delays[1].Due.SetResult();
        await Until(() => _delays.Count == 3);
        Assert.Equal(TimeSpan.FromMinutes(3), _delays[2].Delay); //封顶
        Assert.True(_away.IsAway(_group.SessionId)); //没进展不结束离席
    }

    [Fact]
    public async Task Reminder_RidesEveryAvatarTurn_ButNeverReachesTheGroup()
    {
        const string reminder = "原语级改动可以替我拍";
        _runner.Replies[_avatar.SessionId] = _ => ""; //没写正文

        _away.Start(_group, "目标", null, reminder);
        await Until(() => _delays.Count == 1);
        Assert.Contains(reminder, LastAvatarInput());

        _delays[0].Due.SetResult();
        await Until(() => _delays.Count == 2);
        Assert.Contains(reminder, LastAvatarInput());
        Assert.EndsWith(GroupAvatarTranscript.SilentNote, LastAvatarInput()); //没进展的提示仍在最后

        Assert.DoesNotContain(_group.History, x => x.Text.Contains(reminder));
        Assert.DoesNotContain(_runner.Calls, x => x.Member != _avatar && x.Input.Contains(reminder));
    }

    [Fact]
    public async Task InfiniteMode_IgnoresAllFuses_OnlyManualEnds()
    {
        _settings = Settings with { MaxIdleWaves = 1 };
        _coordinator.EpisodeEnded += _ => _stamp = "same"; //后挂的覆盖前面那个：一直没有新产物
        _runner.Replies[_avatar.SessionId] = _ => "@Alice 再看看";

        Assert.True(_away.Start(_group, "把登录页修了", null, null, true));
        await Until(() => AvatarCalls() >= 3);
        Assert.True(_away.IsAway(_group.SessionId)); //空闲保险丝没炸
        Assert.Empty(_receipts);

        // 捎话只在首轮，之后不再重带
        List<string> avatarInputs = _runner.Calls.Where(x => x.Member == _avatar).Select(x => x.Input).ToList();
        Assert.Contains("把登录页修了", avatarInputs[0]);
        Assert.All(avatarInputs.Skip(1), x => Assert.DoesNotContain("把登录页修了", x));

        _now += TimeSpan.FromHours(2); //时长也到顶了
        _away.End(_group.SessionId);
        GroupAwayReceipt receipt = await NextReceipt();

        Assert.Equal(EGroupAwayEndReason.Manual, receipt.Reason); //还是手动结束，不是保险丝
    }

    [Fact]
    public async Task EmptyGoal_StartsWithoutKickoff()
    {
        _runner.Replies[_avatar.SessionId] = _ => ""; //没写正文
        Assert.True(_away.Start(_group, "", null, "别动文档"));
        await Until(() => _delays.Count == 1);

        string input = LastAvatarInput();
        Assert.DoesNotContain("捎给你的话", input);
        Assert.Contains("别动文档", input);
        Assert.True(_away.IsAway(_group.SessionId));
    }

    [Fact]
    public async Task InfiniteMode_BlockedEndCall_DoesNotEnd_AndDoesNotSwallowItsWords()
    {        _runner.Replies[_avatar.SessionId] = _ => "@Alice 继续";
        _runner.During[_avatar.SessionId] = () =>
        {
            if (AvatarCalls() == 2) AddEndAway("done", "做完了");
            return Task.CompletedTask;
        };

        _away.Start(_group, "目标", null, null, true);
        await Until(() => AvatarCalls() >= 3); //调了结束的那一轮没收场，接着往下走
        Assert.True(_away.IsAway(_group.SessionId));
        Assert.Empty(_receipts);
        // 被拦的那句之后说的话照常进群。无限模式还在连锁追加：读快照，不直接枚举群流水
        Assert.Contains(_coordinator.HistorySnapshot(_group), x => x.Text == "@Alice 继续");

        _away.End(_group.SessionId);
        GroupAwayReceipt receipt = await NextReceipt();
        Assert.Equal(EGroupAwayEndReason.Manual, receipt.Reason);
    }

    [Fact]
    public async Task PushNobodyAnswers_IsAnEmptyPush()
    {
        foreach (string id in _group.GroupMemberSessionIds) _runner.Replies[id] = _ => GroupTranscript.PassReply;
        _runner.Replies[_avatar.SessionId] = _ => "大家继续";

        _away.Start(_group, "目标", null);
        await Until(() => _delays.Count == 1);
        _delays[0].Due.SetResult();
        await Until(() => AvatarCalls() == 2);

        Assert.EndsWith(GroupAvatarTranscript.EmptyPushNote, LastAvatarInput());
    }

    [Fact]
    public async Task Silence_InInfiniteMode_PointsToInfiniteSectionInsteadOfEnding()
    {
        _runner.Replies[_avatar.SessionId] = _ => ""; //没写正文

        _away.Start(_group, "目标", null, null, true);
        await Until(() => _delays.Count == 1);

        _delays[0].Due.SetResult();
        await Until(() => _delays.Count == 2);
        Assert.EndsWith(GroupAvatarTranscript.SilentNoteInfinite, LastAvatarInput());
        Assert.True(_away.IsAway(_group.SessionId)); //没进展不结束离席
    }

    [Fact]
    public async Task EmptyPush_InInfiniteMode_DoesNotTellToEnd()
    {
        foreach (string id in _group.GroupMemberSessionIds) _runner.Replies[id] = _ => GroupTranscript.PassReply;
        _runner.Replies[_avatar.SessionId] = _ => "大家继续";

        _away.Start(_group, "目标", null, null, true);
        await Until(() => _delays.Count == 1);
        _delays[0].Due.SetResult();
        await Until(() => AvatarCalls() == 2);

        Assert.EndsWith(GroupAvatarTranscript.EmptyPushNoteInfinite, LastAvatarInput());
    }

    [Fact]
    public async Task UserStop_WakesTheAvatarAfterTheFixedDelay_WithANote()
    {
        _runner.During[_alice.SessionId] = () =>
        {
            _coordinator.Stop(_group.SessionId);
            return Task.CompletedTask;
        };

        _away.Start(_group, "目标", null);
        // 化身首轮的话已进群、自己也被停下，成员波被停又补一次：两次都是固定延迟，后一次顶掉前一次
        await Until(() => _delays.Count >= 1);
        Assert.Equal(TimeSpan.FromMinutes(5), _delays[^1].Delay);
        Assert.Equal(1, AvatarCalls());

        _runner.During.Remove(_alice.SessionId);
        _runner.Replies[_avatar.SessionId] = _ => ""; //没写正文
        _delays[^1].Due.SetResult();
        await Until(() => AvatarCalls() == 2);
        Assert.EndsWith(GroupAvatarTranscript.StoppedNote(CharacterManager.Instance.UserCharacterName), LastAvatarInput());
    }

    [Fact]
    public async Task NoNewArtifactsForWaves_BlowsTheIdleFuse()
    {
        _settings = Settings with { MaxIdleWaves = 2 };
        _coordinator.EpisodeEnded += _ => _stamp = "same"; //后挂的覆盖前面那个：一直没有新产物
        _runner.Replies[_avatar.SessionId] = _ => "@Alice 再看看";

        _away.Start(_group, "目标", null);
        GroupAwayReceipt receipt = await NextReceipt();

        Assert.Equal(EGroupAwayEndReason.IdleFuse, receipt.Reason);
    }

    [Fact]
    public async Task Overtime_BlowsTheDurationFuseAtTheNextWake()
    {
        _runner.Replies[_avatar.SessionId] = _ => ""; //没写正文
        _away.Start(_group, "目标", null);
        await Until(() => _delays.Count == 1);

        _now += TimeSpan.FromHours(2);
        _delays[0].Due.SetResult();
        GroupAwayReceipt receipt = await NextReceipt();

        Assert.Equal(EGroupAwayEndReason.DurationFuse, receipt.Reason);
        Assert.Equal(1, AvatarCalls());
    }

    [Fact]
    public async Task ManualEnd_CancelsThePendingWake()
    {
        _runner.Replies[_avatar.SessionId] = _ => ""; //没写正文
        _away.Start(_group, "目标", null);
        await Until(() => _delays.Count == 1);

        _away.End(_group.SessionId);
        GroupAwayReceipt receipt = await NextReceipt();

        Assert.Equal(EGroupAwayEndReason.Manual, receipt.Reason);
        Assert.True(_delays[0].Due.Task.IsCanceled);
        Assert.Null(_away.StatusOf(_group.SessionId));
    }

    [Fact]
    public async Task UserPost_CancelsTheCountdown_AndHisWaveWakesTheAvatar()
    {
        _runner.Replies[_avatar.SessionId] = _ => ""; //没写正文
        _away.Start(_group, "目标", null);
        await Until(() => _delays.Count == 1);

        Task posted = _coordinator.PostAsync(_group, "顺便把第三步也做了");
        Assert.True(_delays[0].Due.Task.IsCanceled); //一发言就取消，不等他那一波收场
        await posted;
        await Until(() => AvatarCalls() == 2);

        string input = LastAvatarInput();
        Assert.Contains("顺便把第三步也做了", input);
        Assert.EndsWith(GroupAvatarTranscript.SilentNote, input); //没进展的提示随那次带上
    }

    [Fact]
    public async Task EndedMidTurn_ReceiptWaitsForTheAvatarTurnToSettle()
    {
        _runner.During[_avatar.SessionId] = () =>
        {
            _away.End(_group.SessionId);
            Assert.Empty(_receipts); //化身那一轮还没停稳，回执不出
            return Task.CompletedTask;
        };

        _away.Start(_group, "目标", null);
        GroupAwayReceipt receipt = await NextReceipt();

        Assert.Equal(EGroupAwayEndReason.Manual, receipt.Reason);
        Assert.Single(_group.History, x => GroupAwayReceipt.Of(x) != null);
    }

    [Fact]
    public async Task StoppedWave_DoesNotCountTowardsTheIdleFuse()
    {
        _settings = Settings with { MaxIdleWaves = 1 };
        _coordinator.EpisodeEnded += _ => _stamp = "0"; //一直没有新产物
        _runner.During[_alice.SessionId] = () =>
        {
            _coordinator.Stop(_group.SessionId);
            return Task.CompletedTask;
        };

        _away.Start(_group, "目标", null);
        // 化身首轮边说边被停、成员波被停又补一次：两次都是固定延迟，后一次顶掉前一次
        await Until(() => _delays.Count >= 1);

        Assert.True(_away.IsAway(_group.SessionId));
        Assert.Equal(TimeSpan.FromMinutes(5), _delays[^1].Delay);
    }

    [Fact]
    public async Task Approvals_GoToTheAvatarOnlyWhileAway()
    {
        Assert.Null(_away.ApprovalsFor(_alice, CancellationToken.None));

        _runner.Replies[_avatar.SessionId] = _ => ""; //没写正文
        _away.Start(_group, "目标", null);
        Assert.NotNull(_away.ApprovalsFor(_alice, CancellationToken.None));
        Assert.NotNull(_away.ApprovalsFor(_avatar, CancellationToken.None));

        await Until(() => _delays.Count == 1);
        _away.End(_group.SessionId);
        Assert.Null(_away.ApprovalsFor(_alice, CancellationToken.None));
    }

    private Task Delay(TimeSpan delay, CancellationToken token)
    {
        TaskCompletionSource due = new(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => due.TrySetCanceled(token));
        lock (_delays) _delays.Add((delay, due));
        return due.Task;
    }

    private void AddEndAway(string reason, string summary) =>
        _avatar.History.Add(new ChatMessage(ChatRole.Assistant, [
            new FunctionCallContent("end", EndAwayTool.ToolName,
                new Dictionary<string, object?> { ["reason"] = reason, ["summary"] = summary }),
        ]));

    private int AvatarCalls() => _runner.CallsOf(_avatar);

    private string LastAvatarInput() => _runner.Calls.Last(x => x.Member == _avatar).Input;

    private async Task<GroupAwayReceipt> NextReceipt()
    {
        await Until(() =>
        {
            lock (_receipts) return _receipts.Count > 0;
        });
        lock (_receipts) return _receipts[0];
    }

    private static async Task Until(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not reached");
            await Task.Delay(10);
        }
    }

    private ChatSession Member(string name)
    {
        CharacterData character = new() { CharacterId = name.ToLowerInvariant(), CharacterName = name };
        return Track(new ChatSession(name, character) { IsTransient = true, GroupId = _group.SessionId });
    }

    private ChatSession Track(ChatSession session)
    {
        _sessions[session.SessionId] = session;
        return session;
    }
}
