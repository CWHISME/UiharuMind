using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat.WakeLoops;
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
    private readonly Func<ChatSession, string?, string?, ChatSession> _ensureAvatar;
    private readonly Func<GroupAwaySettings> _settings;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<ChatSession, string> _artifactStamp;
    private readonly GroupAwayApprover _approver;
    private readonly Func<ChatSession, bool> _hasBackgroundWork;
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
    /// <param name="hasBackgroundWork">成员还有后台工作没交回；null 按未了结的工作与待交附注查</param>
    public GroupAwayController(GroupChatCoordinator coordinator,
        Func<ChatSession, string?, string?, ChatSession>? ensureAvatar = null,
        Func<GroupAwaySettings>? settings = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? now = null,
        Func<ChatSession, string>? artifactStamp = null,
        GroupAwayApprover? approver = null,
        Func<ChatSession, bool>? hasBackgroundWork = null)
    {
        _approver = approver ?? new GroupAwayApprover(historyOf: coordinator.HistorySnapshot);
        _hasBackgroundWork = hasBackgroundWork ?? HasBackgroundWork;
        _coordinator = coordinator;
        _ensureAvatar = ensureAvatar ?? GroupAvatar.EnsureFor;
        _settings = settings ?? (() => GroupAwaySettings.From(AgentSettingConfig.Current));
        _delay = delay ?? Task.Delay;
        _now = now ?? (() => DateTimeOffset.Now);
        _artifactStamp = artifactStamp ?? (group => WakeLoop.StampOf(GroupArtifacts.CollectFor(group)));
        _coordinator.EpisodeEnded += OnEpisodeEnded;
        _coordinator.UserPosted += OnUserPosted;
        _coordinator.UserPostAppended += OnUserPostAppended;
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
    /// <param name="avatarCharacterId">化身这次挂的角色；null 为默认化身卡（只定人格风格，职责另有统一注入）</param>
    /// <returns>开始了为 true；已经在离席为 false</returns>
    public bool Start(ChatSession group, string? goal, string? avatarModelName, string? reminder = null,
        bool infinite = false, string? avatarCharacterId = null)
    {
        GroupAwaySession session;
        lock (_sync)
        {
            if (_sessions.ContainsKey(group.SessionId)) return false;
        }

        // 化身与产物指纹都在锁外取（读盘）；之后再确认一次没被别处抢先开
        ChatSession avatar = _ensureAvatar(group, avatarModelName, avatarCharacterId);
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
    /// 更新正在进行的离席：捎话、提醒与无限模式。捎话在化身介入前改才随首轮投递；
    /// 提醒与无限模式每轮现读，随时改随时生效
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <param name="goal">新捎话；null 不改</param>
    /// <param name="reminder">新提醒；null 不改</param>
    /// <param name="infinite">新无限模式；null 不改</param>
    /// <returns>更新了为 true；没在离席为 false</returns>
    public bool Update(string groupId, string? goal, string? reminder, bool? infinite)
    {
        lock (_sync)
        {
            GroupAwaySession? session = _sessions.GetValueOrDefault(groupId);
            if (session == null) return false;
            session.Update(goal, reminder, infinite);
        }

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

    // 用户在群里插了话、化身正跑着：先把新发言即时追加进流水文件，再注入一条<b>系统提示</b>（不灌正文），
    // 化身按提示 Read 流水文件读到真新段。追加与波末收口同一份（AppendLogNow），行号不漂移。
    // 化身闲着不另起一轮：用户那句自己会开一波，波末投递锚点照常带上。
    // 追加失败（append null）就不注入：提示指空会引化身 Read 到旧段，不如让这一轮跑完、下轮锚点再看
    private void OnUserPostAppended(ChatSession group, ChatMessage post, int index)
    {
        GroupAwaySession? session;
        GroupLogAppend? append;
        lock (_sync)
        {
            session = _sessions.GetValueOrDefault(group.SessionId);
            if (session is not { IsEnded: false, IsRunning: true }) return;
            append = AppendLogNow(session);
        }

        if (append == null) return;
        bool seesImages = session.Avatar.ChatModelRunningData?.IsVisionModel == true;
        ChatMessage injection = GroupTranscript.DeliveryMessage(
            GroupAvatarTranscript.UserPostInjection(GroupLogFile.PathOf(group), append.FirstIndex, append.LastIndex,
                append.StartLine, append.EndLine),
            seesImages ? GroupTranscript.ImagesOf(post) : []);
        TryInjectUserPostAsync(session.Avatar, group.SessionId, injection).LogOnFault("inject the user post into the group avatar");
    }

    // 注入用户发言的系统提示：化身那一轮恰好结束就排不进去（false）——信息不丢，波末锚点会指到那一段，
    // 但排不进去要留一条可查的日志，别静默
    private async Task TryInjectUserPostAsync(ChatSession avatar, string groupId, ChatMessage injection)
    {
        if (!await _coordinator.TryInjectAsync(avatar, injection))
            Log.Debug($"Group away can't inject the user post into the running avatar: {groupId}");
    }

    // 追加群流水文件（波末与用户插话共用）：<b>调用方须持 _sync</b>——「读状态 → 追加 → 更新状态」同临界区，
    // 同群两处并发追加时，第二个读到已推进的水位，AppendNew 返回 null，不会把同一段写两遍、行号也不漂移。
    // 没有新发言（或写失败）返回 null；LogLastAppend 只在成功时更新
    private GroupLogAppend? AppendLogNow(GroupAwaySession session, IReadOnlyList<ChatMessage>? groupLog = null)
    {
        if (session.IsEnded) return null;
        groupLog ??= _coordinator.HistorySnapshot(session.Group);
        GroupLogAppend? append = GroupLogFile.AppendNew(session.Group, groupLog, session.LogAppendedUpTo,
            session.LogEndLine + 1);
        if (append != null)
        {
            session.LogLastAppend = append;
            session.LogAppendedUpTo = append.LastIndex + 1;
            session.LogEndLine = append.EndLine;
        }

        return append;
    }

    private void OnEpisodeEnded(GroupEpisodeSummary summary)
    {
        string groupId = summary.Group.SessionId;
        // 快照在 coordinator 的 _locker 里拷贝，与 _sync 不叠加；stamp 也锁外读盘
        IReadOnlyList<ChatMessage> groupLog = _coordinator.HistorySnapshot(summary.Group);
        string stamp = SafeStamp(summary.Group);
        // 空推 = 化身这句开的波里既没有成员接话、用户真身也没回群插话；用户真身说了就不算空推
        bool emptyPush = summary.MemberPostCount == 0 && summary.UserPostCount == 0 && IsAvatarKickoff(summary);
        EGroupAwayEndReason? fuse = null;
        GroupAwaySession? session;
        lock (_sync)
        {
            session = _sessions.GetValueOrDefault(groupId);
            if (session == null) return;

            // 「读状态 → 追加 → 更新状态」同临界区（AppendLogNow）：同群两波并发收场时，第二个等第一个释放后
            // 重读到的 LogAppendedUpTo 已经推进，不会把同一段写两遍、锚点行号也不会逐段漂移。
            // 用户插话的即时追加也走这里（OnUserPostAppended），同一份水位不会双写
            AppendLogNow(session, groupLog);
            // 没有新发言（或追加失败）时保留 LogLastAppend 原值：hasNew 由交付水位判，
            // 已交付的段不会反复标「新发言」；append 失败的日志告警在 GroupLogFile 里

            bool newArtifacts = stamp != session.ArtifactStamp;
            session.ArtifactStamp = stamp;
            // 进展 = 成员接了话或落了新产物：退避归零（等后台的计数不清：成员闲聊不代表后台交回了，
            // 清了它，活跃群里后台一挂起就永远等不到超限那一下硬叫醒）
            if (summary.MemberPostCount > 0 || newArtifacts) session.BackoffLevel = 0;
            // 被用户停下的那一波不算「没有新产物」；无限模式没有空闲保险丝
            if (newArtifacts) session.IdleWaves = 0;
            else if (!summary.Stopped && !session.IsInfinite && ++session.IdleWaves >= session.Settings.MaxIdleWaves)
                fuse = EGroupAwayEndReason.IdleFuse;
        }

        // 判据在 _sync 外现查：禁在锁内调（HasPendingNotes 拿 coordinator 的锁），
        // coordinator 回调 away（EpisodeEnded/UserPosted/UserPostAppended）也都不持它自己的锁，顺序只有这一向
        switch (GroupAwayWakePolicy.Decide(fuse != null, summary.Stopped, emptyPush, _hasBackgroundWork(summary.Group)))
        {
            case EGroupAwayWake.FinishIdle when fuse is { } idle:
                Finish(session, idle, string.Empty);
                break;
            case EGroupAwayWake.WaitStop:
                Schedule(session, session.Settings.StopDelay, GroupAvatarTranscript.StoppedNote(UserName));
                break;
            case EGroupAwayWake.DeferBackground:
                DeferWake(session, null);
                break;
            case EGroupAwayWake.BackoffEmpty:
                NoProgress(session, GroupAvatarTranscript.EmptyPushNote);
                break;
            case EGroupAwayWake.WakeNow:
            default:
                // default 与 WakeNow 同路：Decide 只返回以上五种，这里是防御性兜底，走统一入口重判一次
                RequestWake(session, null);
                break;
        }
    }

    // 成员名下还有后台工作没了结（在跑的后台任务/子代理），或有已跑完、等着叫他开口的附注
    private bool HasBackgroundWork(ChatSession group)
    {
        if (group.GroupMemberSessionIds.Any(PendingWork.Has)) return true;
        return _coordinator.HasPendingNotes(group.SessionId);
    }

    // 想叫化身的统一入口（波末分发、化身跑完重判、延迟到点）：后台没交回就等，不直接开跑，
    // 化身抢跑与成员交回波的竞态缝只剩判据与开跑之间这一步
    private void RequestWake(GroupAwaySession session, string? note)
    {
        if (_hasBackgroundWork(session.Group))
        {
            DeferWake(session, note);
            return;
        }

        // 后台了结（或从没拖过）：等后台的计数清零，下次 defer 从头数
        lock (_sync)
        {
            if (!session.IsEnded) session.DeferCount = 0;
        }

        Wake(session, note);
    }

    // 后台没交回：这次不叫化身，等成员交回的那一波收场时再叫，不让他抢先错序。
    // 化身跑着就记下、跑完收尾重判；否则排一个短延迟兜底——成员交回波若因故没来
    // （如附注波被私聊闸吞掉、一句没说就不发波末事件），到点重判；连续超限则硬叫醒一次
    // （后台可能真卡死了：卡死后的迟发言好过一直静默，化身那轮自会按没进展退避）
    private void DeferWake(GroupAwaySession session, string? note)
    {
        bool avatarRunning;
        bool capped = false;
        TimeSpan recheck;
        lock (_sync)
        {
            if (session.IsEnded) return;
            session.CarryNote(note);
            avatarRunning = session.IsRunning;
            if (avatarRunning) session.WakePending = true;
            // 与空闲保险丝借同一上限：连续等后台太久，真卡死了也要醒一次，不额外加配置。
            // 硬叫醒后计数清零，重新开始等——后台还不交回就再等一轮，不连着吵
            else if (++session.DeferCount > session.Settings.MaxIdleWaves)
            {
                session.DeferCount = 0;
                capped = true;
            }
            recheck = session.Settings.BackoffStart;
        }

        Log.Debug($"Group away defers the avatar: {session.Group.SessionId} has pending background work.");
        RaiseStatusChanged(session.Group.SessionId);
        if (avatarRunning) return;
        if (capped) Wake(session, note);
        else Schedule(session, recheck, null);
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
            if (session.IsRunning)
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
                session.IsRunning = true;
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
            session.IsRunning = false;
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
        }

        // 捎话轮没跑成（用户正在私聊化身、或这一轮失败）：交代还没送达，下次重带
        if (kickoff && turn.Result is EGroupAvatarTurnResult.Busy or EGroupAvatarTurnResult.Failed)
        {
            lock (_sync) session.KickoffDelivered = false;
        }

        // 跑的期间又有一波收场：不论这一轮结局如何，立刻再看一眼（后台没交回就继续等，不硬叫）
        if (pending)
        {
            RequestWake(session, NoteFor(turn.Result));
            return;
        }

        switch (turn.Result)
        {
            case EGroupAvatarTurnResult.Silent:
            case EGroupAvatarTurnResult.Failed:
            case EGroupAvatarTurnResult.Preempted: //化身不再被私聊抢占（私聊即正常消息）；真出现就当没进展兜底，别让离席卡住
                NoProgress(session, NoteFor(turn.Result));
                break;
            case EGroupAvatarTurnResult.Busy:
                // 化身闲着时私聊轮占着闸、离席轮抢不到：不算没进展（捎话重带在上面已安排；Busy 也不计出手次数）。
                // 过一阵再试，没交出去的提示带回去；私聊放闸后下一轮自然能跑
                lock (_sync) session.AvatarTurns--;
                Schedule(session, session.Settings.BackoffStart, note);
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
            session.CarryNote(note);
            token = session.BeginDelay(_now() + delay);
        }

        RaiseStatusChanged(session.Group.SessionId);
        DelayThenWakeAsync(session, delay, token).LogOnFault("wake the group avatar later");
    }

    private async Task DelayThenWakeAsync(GroupAwaySession session, TimeSpan delay, CancellationToken token)
    {
        if (!await WakeLoop.WaitAsync(_delay, delay, token).ConfigureAwait(false)) return;
        lock (_sync)
        {
            if (!session.TryTakeDue(token)) return;
        }

        // 到点时群正在跑一波：那一波收场会叫醒化身，提示留着随那次带上
        if (_coordinator.IsRunning(session.Group.SessionId))
        {
            RaiseStatusChanged(session.Group.SessionId);
            return;
        }

        RequestWake(session, null);
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
            avatarRunning = session.IsRunning;
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
        WakeLoop.Raise(Ended, receipt, "Group away ended");
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

    private string SafeStamp(ChatSession group) => WakeLoop.SafeStamp(_artifactStamp, group);

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
        // 群壳、成员与化身的 $DRAFT 都指同一间草稿目录：锚点写 $DRAFT 简称，不暴露真实绝对路径
        return GroupAvatarTranscript.DeliveryAnchor($"$DRAFT/{GroupLogText.FileName}", kickoff, hasNew,
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

    private void RaiseStatusChanged(string groupId) => WakeLoop.Raise(StatusChanged, groupId, "Group away status");
}
