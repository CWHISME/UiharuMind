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
/// <item>没进展（化身没给出下文、出错、或它开的一波没人接话）不结束，改为延迟唤醒：间隔逐次翻倍、封顶，一有成员接话就归零</item>
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
    /// 开始离席：取（或建）化身、定格参数，捎话与提醒只私下交代给化身（不进群）——
    /// 化身由它的第一轮开第一波，波末自然醒来。捎话可空：没填就没有首轮那份
    /// </summary>
    /// <param name="group">群壳会话（智能体群）</param>
    /// <param name="goal">只给化身看的捎话；null 或空白为没有</param>
    /// <param name="avatarModelName">化身这次用的模型；null 跟随全局</param>
    /// <param name="reminder">只给化身看的重要提醒（成员看不到）；null 或空白为没有</param>
    /// <param name="infinite">无限模式：只有用户手动能结束，保险丝全关、化身调结束工具报错</param>
    /// <returns>开始了为 true；已经在离席为 false</returns>
    public bool Start(ChatSession group, string? goal, string? avatarModelName, string? reminder = null,
        bool infinite = false)
    {
        GroupAwaySession session;
        lock (_sync)
        {
            if (_sessions.ContainsKey(group.SessionId)) return false;
        }

        // 化身与产物指纹都在锁外取（读盘）；之后再确认一次没被别处抢先开
        ChatSession avatar = _ensureAvatar(group, avatarModelName);
        string stamp = SafeStamp(group);
        // 群流水文件：开离席时建/确认，写进全部历史（第三方视角，化身按锚点自己读，不灌正文）
        IReadOnlyList<ChatMessage> groupLog = _coordinator.HistorySnapshot(group);
        int logEndLine = GroupLogFile.Initialize(group, groupLog);
        lock (_sync)
        {
            if (_sessions.ContainsKey(group.SessionId)) return false;
            session = new GroupAwaySession(group, avatar, _settings(), _now(), stamp, goal, reminder, infinite)
            {
                LogAppendedUpTo = groupLog.Count,
                LogEndLine = logEndLine,
                LogDeliveredUpTo = groupLog.Count - 1, //初始化快照已含全部历史：这些不算「新段」
            };
            _sessions[group.SessionId] = session;
        }

        RaiseStatusChanged(group.SessionId);
        // 群正在跑一波：不等它——波末那次唤醒来带首轮（kickoff 还没消费，丢不了）
        if (!_coordinator.IsRunning(group.SessionId)) Wake(session, null);
        return true;
    }

    /// <summary>
    /// 某个群是不是在无限模式的离席里
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <returns>无限模式的离席中为 true</returns>
    public bool IsInfinite(string groupId)
    {
        lock (_sync) return _sessions.GetValueOrDefault(groupId)?.IsInfinite == true;
    }

    /// <summary>
    /// 按化身会话查它所在的离席是不是无限模式（结束工具执行时现问）
    /// </summary>
    /// <param name="avatarSessionId">化身会话标识</param>
    /// <returns>无限模式的离席中为 true</returns>
    public bool IsInfiniteByAvatar(string? avatarSessionId)
    {
        if (string.IsNullOrEmpty(avatarSessionId)) return false;
        lock (_sync) return _sessions.Values.Any(x => x.IsInfinite && x.Avatar.SessionId == avatarSessionId);
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
        // 快照在 coordinator 的 _locker 里拷贝，与 _sync 不叠加；stamp 也锁外读盘
        IReadOnlyList<ChatMessage> groupLog = _coordinator.HistorySnapshot(summary.Group);
        string stamp = SafeStamp(summary.Group);
        bool emptyPush = summary.MemberPostCount == 0 && IsAvatarKickoff(summary);
        EGroupAwayEndReason? fuse = null;
        GroupAwaySession? session;
        lock (_sync)
        {
            session = _sessions.GetValueOrDefault(groupId);
            if (session == null) return;

            // 「读状态 → 追加 → 更新状态」同临界区：同群两波并发收场时，第二个等第一个释放后
            // 重读到的 LogAppendedUpTo 已经推进，不会把同一段写两遍、锚点行号也不会逐段漂移
            GroupLogAppend? append = GroupLogFile.AppendNew(session.Group, groupLog, session.LogAppendedUpTo,
                session.LogEndLine + 1);
            if (append != null)
            {
                session.LogLastAppend = append;
                session.LogAppendedUpTo = append.LastIndex + 1;
                session.LogEndLine = append.EndLine;
            }
            // 没有新发言（或追加失败）时保留 LogLastAppend 原值：hasNew 由交付水位判，
            // 已交付的段不会反复标「新发言」；append 失败的日志告警在 GroupLogFile 里

            bool newArtifacts = stamp != session.ArtifactStamp;
            session.ArtifactStamp = stamp;
            // 进展 = 成员接了话或落了新产物：退避归零
            if (summary.MemberPostCount > 0 || newArtifacts) session.BackoffLevel = 0;
            // 被用户停下的那一波不算「没有新产物」；无限模式没有空闲保险丝
            if (newArtifacts) session.IdleWaves = 0;
            else if (!summary.Stopped && !session.IsInfinite && ++session.IdleWaves >= session.Settings.MaxIdleWaves)
                fuse = EGroupAwayEndReason.IdleFuse;
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
        bool infinite;
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

            infinite = session.IsInfinite;
            if (!infinite && _now() - session.StartedAt >= session.Settings.MaxDuration)
                fuse = EGroupAwayEndReason.DurationFuse;
            else if (!infinite && session.AvatarTurns >= session.Settings.MaxAvatarTurns)
                fuse = EGroupAwayEndReason.TurnsFuse;
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
        // 捎话只在本次离席化身跑成的第一轮交代：没跑成（用户正在私聊它）就下次重带
        bool kickoff;
        string? anchor;
        int deliveredUpToAtBuild;
        lock (_sync)
        {
            kickoff = !session.KickoffDelivered;
            if (kickoff) session.KickoffDelivered = true;
            // 流水文件写失败（LogEndLine=0）时不给锚点，回退 NothingNew——别把化身引向 Read 失败循环
            anchor = session.LogEndLine > 0 ? BuildDeliveryAnchor(session, kickoff) : null;
            // 先捕获本轮要交代到的水位，投递真进了历史才提交（下面 turn 结束后判），
            // 免得 Busy/Failed/Stopped 轮没送到就把段标成已交付，下一轮再也看不见它
            deliveredUpToAtBuild = session.LogAppendedUpTo - 1;
        }

        GroupAvatarTurn turn;
        try
        {
            turn = await _coordinator.RunAvatarAsync(session.Group, session.Avatar,
                    WithBriefing(session, note, kickoff),
                    session.Token, endCallsBlocked: session.IsInfinite,
                    deliveryOverride: anchor)
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
            // 交付水位：投递真进了化身历史才提交（Pushed/Silent/Ended/Preempted）。
            // 提交的是构建时刻的水位，不取当前——化身跑着时若又有新段追加，那些不算已交付
            if (turn.Result is EGroupAvatarTurnResult.Pushed or EGroupAvatarTurnResult.Silent
                or EGroupAvatarTurnResult.Ended or EGroupAvatarTurnResult.Preempted)
            {
                session.LogDeliveredUpTo = Math.Max(session.LogDeliveredUpTo, deliveredUpToAtBuild);
            }
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
            case EGroupAvatarTurnResult.Preempted:
                // 被用户私聊叫停：不计出手次数，私聊结束就接回；期间收场的波也由这次接回一并看
                lock (_sync) session.AvatarTurns--;
                ResumeAfterPrivate(session);
                return;
        }

        // 捎话轮没跑成（用户正在私聊化身、或这一轮失败）：交代还没送达，下次重带
        if (kickoff && turn.Result is EGroupAvatarTurnResult.Busy or EGroupAvatarTurnResult.Failed)
        {
            lock (_sync) session.KickoffDelivered = false;
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
                // 用户正在私聊化身：不算没进展（捎话重带在上面已安排；Busy 也不计出手次数）。
                // 正等着接回的，私聊一结束就再接；否则过一阵再试，没交出去的提示带回去
                lock (_sync) session.AvatarTurns--;
                if (_coordinator.AvatarResumeOf(session.Group.SessionId) == EGroupAvatarResume.Pending)
                    ResumeAfterPrivate(session);
                else
                    Schedule(session, session.Settings.BackoffStart, note);
                break;
            //Pushed：它那句开的一波收场时会再叫醒它
        }
    }

    // 私聊放闸就接回；等的期间用户停了群，按停下处理（过一阵再叫），已被别的一轮接过就不再叫
    private void ResumeAfterPrivate(GroupAwaySession session)
    {
        lock (_sync)
        {
            if (session.ResumeArmed) return;
            session.ResumeArmed = true;
        }

        GroupMemberTurnGate.ResumeAfter(session.Avatar.SessionId, () =>
        {
            lock (_sync) session.ResumeArmed = false;
            switch (_coordinator.AvatarResumeOf(session.Group.SessionId))
            {
                case EGroupAvatarResume.Pending:
                    Wake(session, GroupTranscript.PrivateResumeNote);
                    break;
                case EGroupAvatarResume.Stopped:
                    _coordinator.ClearAvatarResume(session.Group.SessionId);
                    Schedule(session, session.Settings.StopDelay, GroupAvatarTranscript.StoppedNote(UserName));
                    break;
            }
        });
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

    /// <summary>
    /// 化身这一轮的投递锚点（第三方视角）：不灌群发言正文，只给流水文件位置 + 新段行号 + 身份点破。
    /// 在锁内调用（读 <see cref="GroupAwaySession.LogLastAppend"/>）
    /// </summary>
    /// <param name="session">离席会话</param>
    /// <param name="kickoff">首轮：流水已含全部历史，后面这些话是新的</param>
    /// <returns>锚点正文</returns>
    private static string BuildDeliveryAnchor(GroupAwaySession session, bool kickoff)
    {
        GroupLogAppend? last = session.LogLastAppend;
        // hasNew = 有一段的最后一条还没被锚点交代过：交付水位以外的才算「新发言」，
        // 静默退避重唤不会反复把同一段标成新段
        bool hasNew = last != null && last.LastIndex > session.LogDeliveredUpTo;
        return GroupAvatarTranscript.DeliveryAnchor(GroupLogFile.PathOf(session.Group), kickoff, hasNew,
            last?.FirstIndex ?? 0, last?.LastIndex ?? 0, last?.StartLine ?? 0, last?.EndLine ?? 0,
            session.LogEndLine);
    }

    // 提醒与无限模式每一轮都带，捎话只在首轮带：化身的历史跨离席保留，
    // 提醒只靠第一轮那一次会分不清哪份作数；捎话是用户原话，压缩的回查段会原样留着
    private static string? WithBriefing(GroupAwaySession session, string? note, bool kickoff)
    {
        string? briefing = GroupAvatarTranscript.BriefingNote(UserName, session.Goal, session.Reminder,
            session.IsInfinite, kickoff);
        if (briefing == null) return note;
        return note == null ? briefing : briefing + "\n\n" + note;
    }

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
