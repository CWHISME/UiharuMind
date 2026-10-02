using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Chat.Group.Away;

/// <summary>
/// 离席（ADR 0055）：用户打开后，每一波结束都叫醒化身，由它替用户拍板、决定接着推还是结束。
///
/// <list type="bullet">
/// <item>结束只有四条路：化身调结束工具、用户手动关、保险丝（时长 / 出手次数 / 连续没有新产物）、应用重启（只在内存）</item>
/// <item>没进展（化身沉默、出错、或它开的一波没人接话）不结束，改为延迟唤醒：间隔逐次翻倍、封顶，一有成员接话就归零</item>
/// <item>用户按了停止（没关离席）：固定延迟后再唤醒；倒计时里用户一发言就取消，由他那一波收场时叫醒</item>
/// </list>
/// 一个群同一时刻至多一轮化身：化身在跑时又有一波收场（多半是它自己那句开的），记下来，跑完再叫
/// </summary>
public sealed class GroupAwayController
{
    private readonly GroupChatCoordinator _coordinator;
    private readonly Func<ChatSession, string?, ChatSession> _ensureAvatar;
    private readonly Func<GroupAwaySettings> _settings;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<ChatSession, string> _artifactStamp;
    private readonly GroupAwayApprover _approver;
    private readonly object _sync = new();
    private readonly Dictionary<string, GroupAwaySession> _sessions = new(); //群 → 正在进行的离席

    /// <summary>应用里的那一个：成员（与化身）的审批在离席期间改由它接</summary>
    public static GroupAwayController Instance { get; } = CreateInstance();

    /// <summary>
    /// 构造（测试用来换掉时间、化身与产物的取法）
    /// </summary>
    /// <param name="coordinator">群聊调度宿主</param>
    /// <param name="ensureAvatar">取或建群的化身会话；null 走 <see cref="GroupAvatar.EnsureFor"/></param>
    /// <param name="settings">离席参数；null 取全局设置</param>
    /// <param name="delay">等一段时间；null 走 <see cref="Task.Delay(TimeSpan, CancellationToken)"/></param>
    /// <param name="now">当前时刻；null 取本地时间</param>
    /// <param name="artifactStamp">本群产物的指纹（变了即有新产物）；null 按 <see cref="GroupArtifacts.CollectFor"/> 算</param>
    /// <param name="approver">离席期间的审批通道；null 请化身判</param>
    public GroupAwayController(GroupChatCoordinator coordinator,
        Func<ChatSession, string?, ChatSession>? ensureAvatar = null,
        Func<GroupAwaySettings>? settings = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? now = null,
        Func<ChatSession, string>? artifactStamp = null,
        GroupAwayApprover? approver = null)
    {
        _approver = approver ?? new GroupAwayApprover(historyOf: coordinator.HistorySnapshot);
        _coordinator = coordinator;
        _ensureAvatar = ensureAvatar ?? GroupAvatar.EnsureFor;
        _settings = settings ?? (() => GroupAwaySettings.From(AgentSettingConfig.Current));
        _delay = delay ?? Task.Delay;
        _now = now ?? (() => DateTimeOffset.Now);
        _artifactStamp = artifactStamp ?? ArtifactStampOf;
        _coordinator.EpisodeEnded += OnEpisodeEnded;
        _coordinator.UserPosted += OnUserPosted;
    }

    /// <summary>某个群的离席状态变了（开始、化身开跑/跑完、延迟唤醒排上/取消、结束）。⚠️ 可能来自后台线程</summary>
    public event Action<string>? StatusChanged;

    /// <summary>一次离席结束了，交出回执的原料。⚠️ 可能来自后台线程</summary>
    public event Action<GroupAwayReceipt>? Ended;

    /// <summary>
    /// 某个群此刻的离席状态
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <returns>状态；没在离席为 null</returns>
    public GroupAwayStatus? StatusOf(string groupId)
    {
        lock (_sync) return _sessions.GetValueOrDefault(groupId)?.Snapshot();
    }

    /// <summary>
    /// 某个群是不是在离席
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <returns>在离席为 true</returns>
    public bool IsAway(string groupId)
    {
        lock (_sync) return _sessions.ContainsKey(groupId);
    }

    /// <summary>
    /// 开始离席：取（或建）化身、定格参数，把目标以用户的名义发进群——群闲着就由它开第一波，波末化身醒来
    /// </summary>
    /// <param name="group">群壳会话（智能体群）</param>
    /// <param name="goal">目标（可含备注）</param>
    /// <param name="avatarModelName">化身这次用的模型；null 跟随全局</param>
    /// <returns>开始了为 true；已经在离席为 false</returns>
    public bool Start(ChatSession group, string goal, string? avatarModelName)
    {
        if (string.IsNullOrWhiteSpace(goal)) throw new ArgumentException("An away session needs a goal.", nameof(goal));

        lock (_sync)
        {
            if (_sessions.ContainsKey(group.SessionId)) return false;
        }

        // 化身与产物指纹都在锁外取（读盘）；之后再确认一次没被别处抢先开
        ChatSession avatar = _ensureAvatar(group, avatarModelName);
        string stamp = SafeStamp(group);
        lock (_sync)
        {
            if (_sessions.ContainsKey(group.SessionId)) return false;
            _sessions[group.SessionId] = new GroupAwaySession(group, avatar, _settings(), _now(), stamp);
        }

        RaiseStatusChanged(group.SessionId);
        _coordinator.PostAsync(group, GroupAvatarTranscript.GoalPost(goal)).LogOnFault("post the away goal");
        return true;
    }

    /// <summary>
    /// 用户手动结束离席。化身正在跑的那一轮随之停下；成员正在跑的那一波不受影响
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    public void End(string groupId)
    {
        GroupAwaySession? session;
        lock (_sync) session = _sessions.GetValueOrDefault(groupId);
        if (session != null) Finish(session, EGroupAwayEndReason.Manual, string.Empty);
    }

    /// <summary>
    /// 不等延迟，立刻唤醒化身（界面「立即唤醒」）。群正在跑一波时不叫，那一波收场自然会叫
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    public void WakeNow(string groupId)
    {
        GroupAwaySession? session;
        lock (_sync) session = _sessions.GetValueOrDefault(groupId);
        if (session == null || _coordinator.IsRunning(groupId)) return;
        Wake(session, null);
    }

    /// <summary>
    /// 记下离席期间化身点过的一条审批（进回执）
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <param name="approval">那条审批</param>
    public void RecordApproval(string groupId, GroupAwayApproval approval)
    {
        lock (_sync) _sessions.GetValueOrDefault(groupId)?.Approvals.Add(approval);
    }

    /// <summary>
    /// 群成员（或化身）这一轮遇到审批时问一声：所在的群正在离席，就由化身接；否则照旧等用户点
    /// </summary>
    /// <param name="requester">请求审批的会话</param>
    /// <param name="cancellationToken">它这一轮被停时取消</param>
    /// <returns>审批通道；没在离席为 null</returns>
    public ApprovalResolver? ApprovalsFor(ChatSession requester, CancellationToken cancellationToken)
    {
        GroupAwaySession? session;
        lock (_sync) session = requester.GroupId is { } groupId ? _sessions.GetValueOrDefault(groupId) : null;
        if (session == null) return null;

        string id = session.Group.SessionId;
        return _approver.Create(session.Group, session.Avatar, requester, x => RecordApproval(id, x), cancellationToken);
    }

    private static GroupAwayController CreateInstance()
    {
        GroupAwayController controller = new(GroupChatCoordinator.Instance);
        HeadlessGroupMemberTurnRunner.AwayApprovals = controller.ApprovalsFor;
        return controller;
    }

    // 倒计时里用户发了言：取消倒计时，由他那一波收场时叫醒化身（提示留着随那次带上）
    private void OnUserPosted(string groupId)
    {
        bool cancelled;
        lock (_sync) cancelled = _sessions.GetValueOrDefault(groupId)?.CancelDelay() == true;
        if (cancelled) RaiseStatusChanged(groupId);
    }

    private void OnEpisodeEnded(GroupEpisodeSummary summary)
    {
        string groupId = summary.Group.SessionId;
        lock (_sync)
        {
            if (!_sessions.ContainsKey(groupId)) return;
        }

        string stamp = SafeStamp(summary.Group); //读盘，锁外做
        bool emptyPush = summary.MemberPostCount == 0 && IsAvatarKickoff(summary);
        GroupAwaySession? session;
        EGroupAwayEndReason? fuse = null;
        lock (_sync)
        {
            session = _sessions.GetValueOrDefault(groupId);
            if (session == null) return;

            bool newArtifacts = stamp != session.ArtifactStamp;
            session.ArtifactStamp = stamp;
            // 进展 = 成员接了话或落了新产物：退避归零
            if (summary.MemberPostCount > 0 || newArtifacts) session.BackoffLevel = 0;
            // 被用户停下的那一波不算「没有新产物」
            if (newArtifacts) session.IdleWaves = 0;
            else if (!summary.Stopped && ++session.IdleWaves >= session.Settings.MaxIdleWaves) fuse = EGroupAwayEndReason.IdleFuse;
        }

        if (fuse is { } reason)
        {
            Finish(session, reason, string.Empty);
        }
        else if (summary.Stopped)
        {
            Schedule(session, session.Settings.StopDelay, GroupAvatarTranscript.StoppedNote(UserName));
        }
        else if (emptyPush)
        {
            NoProgress(session, GroupAvatarTranscript.EmptyPushNote);
        }
        else
        {
            Wake(session, null);
        }
    }

    // note 记进「带给下一轮的提示」：化身正在跑就留到它跑完再叫的那一轮，延迟唤醒排着的也一并带上
    private void Wake(GroupAwaySession session, string? note)
    {
        EGroupAwayEndReason? fuse = null;
        lock (_sync)
        {
            if (session.IsEnded) return;
            session.CancelDelay();
            session.CarryNote(note);
            if (session.IsAvatarRunning)
            {
                // 化身还在跑：多半是它自己那句开的一波已经收场。跑完再叫
                session.WakePending = true;
                return;
            }

            if (_now() - session.StartedAt >= session.Settings.MaxDuration) fuse = EGroupAwayEndReason.DurationFuse;
            else if (session.AvatarTurns >= session.Settings.MaxAvatarTurns) fuse = EGroupAwayEndReason.TurnsFuse;
            else
            {
                session.IsAvatarRunning = true;
                session.AvatarTurns++;
                note = session.TakeNote();
            }
        }

        if (fuse is { } reason)
        {
            Finish(session, reason, string.Empty);
            return;
        }

        RaiseStatusChanged(session.Group.SessionId);
        RunAvatarAsync(session, note).LogOnFault("run the group avatar");
    }

    private async Task RunAvatarAsync(GroupAwaySession session, string? note)
    {
        GroupAvatarTurn turn;
        try
        {
            turn = await _coordinator.RunAvatarAsync(session.Group, session.Avatar, note, session.Token)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error($"Group avatar turn failed: {e}");
            turn = new GroupAvatarTurn(EGroupAvatarTurnResult.Failed);
        }

        bool pending;
        bool ended;
        lock (_sync)
        {
            session.IsAvatarRunning = false;
            pending = session.WakePending;
            session.WakePending = false;
            ended = session.IsEnded;
        }

        // 离席在这一轮中途结束了：回执等到这一轮停稳才出，免得边读化身历史边被追加
        if (ended)
        {
            Emit(session);
            return;
        }

        RaiseStatusChanged(session.Group.SessionId);
        switch (turn.Result)
        {
            case EGroupAvatarTurnResult.Ended:
                Finish(session, turn.End!.Reason == EAwayEndRequest.Done ? EGroupAwayEndReason.Done : EGroupAwayEndReason.NeedsUser,
                    turn.End.Summary);
                return;
            case EGroupAvatarTurnResult.Stopped:
                Schedule(session, session.Settings.StopDelay, GroupAvatarTranscript.StoppedNote(UserName));
                return;
        }

        // 跑的期间又有一波收场：不论这一轮结局如何，立刻再看一眼
        if (pending)
        {
            Wake(session, NoteFor(turn.Result));
            return;
        }

        switch (turn.Result)
        {
            case EGroupAvatarTurnResult.Silent:
            case EGroupAvatarTurnResult.Failed:
                NoProgress(session, NoteFor(turn.Result));
                break;
            case EGroupAvatarTurnResult.Busy:
                // 用户正在私聊化身：过一阵再试，不算没进展
                Schedule(session, session.Settings.BackoffStart, null);
                break;
            //Pushed：它那句开的一波收场时会再叫醒它
        }
    }

    private void NoProgress(GroupAwaySession session, string? note)
    {
        TimeSpan delay;
        lock (_sync) delay = session.Settings.BackoffOf(session.BackoffLevel++);
        Schedule(session, delay, note);
    }

    private void Schedule(GroupAwaySession session, TimeSpan delay, string? note)
    {
        CancellationToken token;
        lock (_sync)
        {
            if (session.IsEnded) return;
            session.CancelDelay();
            session.CarryNote(note);
            token = session.BeginDelay(_now() + delay);
        }

        RaiseStatusChanged(session.Group.SessionId);
        DelayThenWakeAsync(session, delay, token).LogOnFault("wake the group avatar later");
    }

    private async Task DelayThenWakeAsync(GroupAwaySession session, TimeSpan delay, CancellationToken token)
    {
        try
        {
            await _delay(delay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_sync)
        {
            if (token.IsCancellationRequested || session.IsEnded) return;
            session.CancelDelay();
        }

        // 到点时群正在跑一波：那一波收场会叫醒化身，提示留着随那次带上
        if (_coordinator.IsRunning(session.Group.SessionId))
        {
            RaiseStatusChanged(session.Group.SessionId);
            return;
        }

        Wake(session, null);
    }

    // 标记结束、摘掉登记；化身正在跑就等它那一轮停稳再出回执（RunAvatarAsync 收尾时补出）
    private void Finish(GroupAwaySession session, EGroupAwayEndReason reason, string summary)
    {
        bool avatarRunning;
        lock (_sync)
        {
            if (session.IsEnded) return;
            session.End(reason, summary);
            _sessions.Remove(session.Group.SessionId);
            avatarRunning = session.IsAvatarRunning;
        }

        RaiseStatusChanged(session.Group.SessionId);
        if (!avatarRunning) Emit(session);
    }

    // 出回执：落进群流水（用户回来先看这一张，界面没开着也不丢），再通报
    private void Emit(GroupAwaySession session)
    {
        IReadOnlyList<ChatMessage> groupLog = _coordinator.HistorySnapshot(session.Group);
        List<GroupAwayApproval> approvals;
        lock (_sync) approvals = session.Approvals.ToList();
        GroupAwayReceipt receipt = new(session.Group.SessionId, session.Avatar.SessionId, session.EndReason!.Value,
            session.EndSummary, session.StartedAt, _now(), session.AvatarTurns,
            AvatarPostIndices(groupLog, session.Avatar.SessionId, session.HistoryStart), approvals,
            AvatarActions(session));
        session.Dispose();

        _coordinator.AppendAwayReceipt(session.Group,
            session.EndSummary.Length > 0 ? session.EndSummary : GroupAvatarTranscript.AwayEndedText, receipt.ToJson());
        try
        {
            Ended?.Invoke(receipt);
        }
        catch (Exception e)
        {
            Log.Error($"Group away ended handler failed: {e}");
        }
    }

    // 化身这一轮已停稳，但用户可能正私聊它：读不出就不列，回执照出
    private static IReadOnlyList<string> AvatarActions(GroupAwaySession session)
    {
        try
        {
            return GroupAvatarTurn.ActionsOf(session.Avatar.History.Skip(session.AvatarHistoryStart).ToList());
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    private static string? NoteFor(EGroupAvatarTurnResult result) => result switch
    {
        EGroupAvatarTurnResult.Silent => GroupAvatarTranscript.SilentNote,
        EGroupAvatarTurnResult.Failed => GroupAvatarTranscript.FailedNote,
        _ => null,
    };

    private bool IsAvatarKickoff(GroupEpisodeSummary summary)
    {
        if (summary.KickoffPostIndex is not { } index) return false;
        IReadOnlyList<ChatMessage> groupLog = _coordinator.HistorySnapshot(summary.Group);
        return index < groupLog.Count && ChatMessageAnnotations.GroupAvatarPostOf(groupLog[index]) != null;
    }

    private static IReadOnlyList<int> AvatarPostIndices(IReadOnlyList<ChatMessage> groupLog, string avatarSessionId, int start)
    {
        List<int> indices = [];
        for (int i = start; i < groupLog.Count; i++)
        {
            if (ChatMessageAnnotations.GroupAvatarPostOf(groupLog[i]) == avatarSessionId) indices.Add(i);
        }

        return indices;
    }

    private string SafeStamp(ChatSession group)
    {
        try
        {
            return _artifactStamp(group);
        }
        catch (Exception e)
        {
            Log.Warning($"Group artifacts stamp failed: {e.Message}");
            return string.Empty;
        }
    }

    // 指纹 = 件数 + 最近一次改动：新建与再次改动都算有新产物
    private static string ArtifactStampOf(ChatSession group)
    {
        IReadOnlyList<GroupArtifact> artifacts = GroupArtifacts.CollectFor(group);
        DateTimeOffset latest = artifacts.Count == 0 ? DateTimeOffset.MinValue : artifacts.Max(x => x.LastWrite);
        return $"{artifacts.Count}|{latest.UtcTicks}";
    }

    private static string UserName => CharacterManager.Instance.UserCharacterName;

    private void RaiseStatusChanged(string groupId)
    {
        try
        {
            StatusChanged?.Invoke(groupId);
        }
        catch (Exception e)
        {
            Log.Error($"Group away status handler failed: {e}");
        }
    }
}
