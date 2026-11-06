using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.WakeLoops;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core.Instances;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Chat.Single.GoalMode;

/// <summary>
/// 单聊目标模式：给一个 agent 会话设目标，模型自动停下来后按调度规则自动唤醒继续推，
/// 直到模型自判结束（调隐藏扩展 goal.end）或用户手动结束。
///
/// <list type="bullet">
/// <item>无保险丝：模型自判结束或用户手动结束是仅有的两条出路（运行期不设时长/轮数上限）</item>
/// <item>没进展（产物指纹没变）不结束，只把下次唤醒再退避一段：逐次翻倍、封顶，一有进展就归零</item>
/// <item>调度规则可多条并存：Daily / OneShot / Interval / Immediate 各自算下次触发时刻，取最近；OneShot 触发后作废</item>
/// <item>无人值守的后台活只归主实例（ADR 0064）：非主实例不恢复、不落盘、开不了</item>
/// </list>
/// 一轮结束的信号从 <see cref="SessionManager.Running"/>（<see cref="SessionRunRegistry.StateChanged"/>）取；
/// 唤醒轮复用 <see cref="SessionWakeTurn.RunAsync"/>（无用户消息、审批弹回会话窗口），结束即取消
/// </summary>
public sealed class GoalModeController
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<ChatSession, string> _artifactStamp;
    private readonly Func<string, string, CancellationToken, Action?, Task> _wakeTurn;
    private readonly object _sync = new();
    private readonly Dictionary<string, GoalModeSession> _sessions = new(); //会话 → 进行中的目标模式
    private readonly Dictionary<string, GoalModeSettings> _lastSettings = new(); //会话 → 最近一次设置（结束不删，Setup 弹窗回填用）

    /// <summary>应用里的那一个</summary>
    public static GoalModeController Instance { get; } = CreateInstance();

    /// <summary>某个会话的目标模式状态变了（开始、开跑/跑完、延迟唤醒排上/取消、结束）。⚠️ 可能来自后台线程</summary>
    public event Action<string>? StatusChanged;

    /// <summary>一次目标模式结束了，交出回执。⚠️ 可能来自后台线程</summary>
    public event Action<GoalModeReceipt>? Ended;

    /// <summary>
    /// 构造（测试用来换掉时间、指纹与唤醒执行者）
    /// </summary>
    /// <param name="delay">等一段时间；null 走 <see cref="Task.Delay(TimeSpan, CancellationToken)"/></param>
    /// <param name="now">当前时刻；null 取本地时间</param>
    /// <param name="artifactStamp">会话产物指纹（变了即有新产物）；null 按件数+最近改动算</param>
    /// <param name="wakeTurn">让会话再跑一轮（抢到会话后才执行 onStarted 里的注入）；null 走 <see cref="SessionWakeTurn.RunAsync"/></param>
    /// <param name="primary">是否主实例；null 取 <see cref="AppInstance.IsPrimary"/></param>
    public GoalModeController(Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? now = null, Func<ChatSession, string>? artifactStamp = null,
        Func<string, string, CancellationToken, Action?, Task>? wakeTurn = null, bool? primary = null)
    {
        _delay = delay ?? Task.Delay;
        _now = now ?? (() => DateTimeOffset.Now);
        _artifactStamp = artifactStamp ?? (session => WakeLoop.StampOf(GroupArtifacts.CollectForSession(session)));
        _wakeTurn = wakeTurn ?? SessionWakeTurn.RunAsync;
        IsOwner = primary ?? AppInstance.IsPrimary;
        if (!IsOwner)
        {
            Log.Debug("Goal mode is owned by another instance; this one does not run it.");
            return;
        }

        SessionManager.Instance.Running.StateChanged += OnStateChanged;
        Restore();
    }

    /// <summary>本实例管不管目标模式：非主实例为 false（ADR 0064），界面据此不让开</summary>
    public bool IsOwner { get; }

    /// <summary>某个会话此刻的目标模式状态；没在为 null</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>状态；没在目标模式为 null</returns>
    public GoalModeStatus? StatusOf(string sessionId)
    {
        lock (_sync) return _sessions.GetValueOrDefault(sessionId)?.Snapshot();
    }

    /// <summary>
    /// 某个会话最近一次的目标模式设置（目标、调度规则、自动同意审批）。结束目标模式不删：
    /// 设置弹窗在没进行中状态时回填它，用户设过的东西结束后仍看得到
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>最近设置；从没开过目标模式为 null</returns>
    public GoalModeSettings? LastSettingsOf(string sessionId)
    {
        lock (_sync) return _lastSettings.GetValueOrDefault(sessionId);
    }

    /// <summary>某个会话是不是在目标模式里（SessionWakeTurn 的封顶豁免据此放行）</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>在为 true</returns>
    public bool IsActive(string sessionId)
    {
        lock (_sync) return _sessions.ContainsKey(sessionId);
    }

    /// <summary>
    /// 某个会话的目标模式是不是勾了自动同意审批：这个会话的审批一条都不问（唤醒轮与用户自己那一轮
    /// 都一样），没窗口那头按同意收口、有窗口那头把卡片收成已同意。⚠️ 可能来自后台线程
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>勾了为 true</returns>
    public bool IsAutoApprove(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        lock (_sync) return _sessions.TryGetValue(sessionId, out GoalModeSession? session) && session.AutoApproveApprovals;
    }

    /// <summary>
    /// 这个会话勾了自动同意审批时，给出无人值守的审批通道；没勾为 null。
    ///
    /// 「问不问人」只此一处说了算：会话窗口与唤醒轮的无人值守策略都向它问（见
    /// <c>Features.Conversation.WakeApprovalHosts</c>），两边不会各判一套。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>审批通道；没勾为 null</returns>
    public ApprovalResolver? ResolverFor(string? sessionId) =>
        IsAutoApprove(sessionId) ? GoalModeApproval.Create(sessionId!) : null;

    /// <summary>全部进行中的目标模式（统一清单用）</summary>
    /// <returns>状态列表</returns>
    public IReadOnlyList<GoalModeStatus> ActiveStatuses()
    {
        lock (_sync) return _sessions.Values.Select(x => x.Snapshot()).ToList();
    }

    /// <summary>
    /// 开始目标模式：登记目标、立刻跑第一轮（模型正在跑就不抢，记成欠着，那一轮收场立刻接）
    /// </summary>
    /// <param name="session">会话（agent 档）</param>
    /// <param name="settings">目标与调度规则、自动同意审批</param>
    /// <returns>开始了为 true；已经在目标模式或本实例不管目标模式为 false</returns>
    public bool Start(ChatSession session, GoalModeSettings settings)
    {
        if (!IsOwner) return false;
        GoalModeSession created;
        DateTimeOffset now;
        lock (_sync)
        {
            if (_sessions.ContainsKey(session.SessionId)) return false;
            now = _now();
            created = new GoalModeSession(session, settings.Goal, EffectiveRules(settings.Rules), now,
                WakeLoop.SafeStamp(_artifactStamp, session), settings.AutoApproveApprovals ?? false);
            _sessions[session.SessionId] = created;
            _lastSettings[session.SessionId] = new GoalModeSettings(created.Goal, created.Rules, created.AutoApproveApprovals);
        }

        Save();
        SaveRecent();
        RaiseStatusChanged(session.SessionId);
        // 目标注入随首轮走（见 RunClaimedTurn）：会话正被用户那一轮占着时，注入也不塞进那一轮
        Wake(created);
        return true;
    }

    /// <summary>
    /// 更新目标、调度规则与自动同意。目标改动不落消息：模型在下次唤醒时经带当前目标的提醒读到。
    /// 规则改了且没在跑就按新规则重排下次唤醒；正在跑的，那一轮收场时按新规则排
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="settings">新参数；<see cref="GoalModeSettings.Goal"/> 为 null 表示不改</param>
    /// <returns>更新了为 true；没在目标模式为 false</returns>
    public bool Update(string sessionId, GoalModeSettings settings)
    {
        GoalModeSession? session;
        bool reschedule;
        lock (_sync)
        {
            session = _sessions.GetValueOrDefault(sessionId);
            if (session == null) return false;
            bool rulesChanged = session.Update(settings.Goal, settings.Rules == null ? null : EffectiveRules(settings.Rules),
                settings.AutoApproveApprovals);
            reschedule = rulesChanged && !session.IsRunning;
            // 规则换了：欠下的那一次（旧规则的一个时刻）不再相干；正在跑的旧延迟也一并撤掉，
            // 否则它会用旧规则排下的时刻去记一笔新规则下的债。收场时按新规则重排就是
            if (rulesChanged)
            {
                session.OwedWake = false;
                session.OwedSince = null;
                session.ScheduledWakeAt = null;
                session.CancelDelay();
            }
            // 更新后的实际生效值也记进最近设置（空规则被换成默认循环等，弹窗回填显示生效的那条）
            _lastSettings[sessionId] = new GoalModeSettings(session.Goal, session.Rules, session.AutoApproveApprovals);
        }

        Save();
        SaveRecent();
        if (reschedule) ScheduleNext(session);
        else RaiseStatusChanged(sessionId);
        return true;
    }

    /// <summary>不等调度，立刻唤醒。会话正忙就不叫，这一次诉求记成欠着，这一轮收场就补</summary>
    /// <param name="sessionId">会话标识</param>
    public void WakeNow(string sessionId)
    {
        GoalModeSession? session;
        lock (_sync) session = _sessions.GetValueOrDefault(sessionId);
        if (session == null) return;
        // 不在这里先查忙：忙与否、要不要欠，全交给 Wake——否则这一个提前拦就把「欠一次」拦掉了
        Wake(session);
    }

    /// <summary>用户手动结束。目标模式唤醒的那一轮正在跑就随之停下</summary>
    /// <param name="sessionId">会话标识</param>
    public void End(string sessionId)
    {
        GoalModeSession? session;
        lock (_sync) session = _sessions.GetValueOrDefault(sessionId);
        if (session != null) Finish(session, EGoalModeEndReason.Manual, string.Empty);
    }

    private static GoalModeController CreateInstance() => new();

    private static IReadOnlyList<GoalModeScheduleRule> EffectiveRules(IReadOnlyList<GoalModeScheduleRule>? rules) =>
        rules is { Count: > 0 } ? rules : [GoalModeScheduleRule.Default];

    // 一轮收场（从非空闲回到空闲）：不论是唤醒轮还是用户自己发的那一轮，都从这里接着排下一次唤醒。
    // 体内包一层：这条链跑在运行登记处的同步广播栈里（用户轮的 finally 里），抛出去会改掉那一轮的收尾
    private void OnStateChanged(string sessionId)
    {
        try
        {
            OnRunStateChanged(sessionId);
        }
        catch (Exception e)
        {
            Log.Error($"Goal mode state change failed: session={sessionId}: {e}");
        }
    }

    private void OnRunStateChanged(string sessionId)
    {
        GoalModeSession? session;
        bool ended = false;
        lock (_sync)
        {
            session = _sessions.GetValueOrDefault(sessionId);
            if (session == null) return;
            ESessionRunState state = SessionManager.Instance.Running.StateOf(sessionId);
            // NotifyStateChanged 这类不改变状态的广播滤掉
            if (state == ESessionRunState.Idle && session.LastRunState != ESessionRunState.Idle) ended = true;
            session.LastRunState = state;
        }

        if (ended) OnTurnEnded(session!);
    }

    private void OnTurnEnded(GoalModeSession session)
    {
        ChatSession chat = session.Session;
        // 模型自判结束：这一轮里调了 goal.end，认出即收场
        if (FindEndCall(chat, session.HistoryStart) is { } end)
        {
            Finish(session, end.Reason, end.Summary);
            return;
        }

        string stamp = WakeLoop.SafeStamp(_artifactStamp, chat);
        bool owed;
        bool settled;
        DateTimeOffset now = _now();
        lock (_sync)
        {
            if (session.IsEnded) return;
            session.BackoffLevel = stamp != session.ArtifactStamp ? 0 : session.BackoffLevel + 1;
            session.ArtifactStamp = stamp;
            session.LastStop = now;
            // 本循环自己那一轮还没收尾（收场广播早于它的尾巴）：排程交给尾巴——
            // 那儿 IsRunning 已清，谁排谁收也只剩一个排程者（账也留到那儿结）
            settled = !session.IsRunning;
            owed = settled && ShouldCashOwed(session, now);
            // 不补就把账结清（不结的话，间隔类的那次会在下一次收场又被误当墙钟债）；
            // 要补则留着——由 Wake 认领时清，认领落空才还得回来（见 RunTurnAsync）
            if (settled && !owed)
            {
                session.OwedWake = false;
                session.OwedSince = null;
            }
        }

        // 落盘是尽力而为（<c>SaveUtility</c> 自己兜异常并记日志，抛不出来），不会挡住下面的排程
        Save();
        if (!settled) return;
        if (owed) Wake(session);
        else ScheduleNext(session);
    }

    // 到点撞忙欠下的那一次还该不该补（⚠️ 调用方持 _sync）：与墙钟无关的欠账（首轮 / 手动唤醒）无条件补；
    // 因撞忙欠下的，得看从那一刻到现在是否越过了某条墙钟规则的触发时刻——间隔类的话按新的停下重算就行。
    //
    // 口径：<b>一个忙碌窗口至多补一轮</b>——窗口里跨过两个墙钟时刻也只补一次（连着补两轮对用户更吵，
    // 且这种窗口很罕见）；没被单独兑现的那个时刻，看它自己的规则还算不算数（一次性过期就作废）。
    // 补的那一轮没真跑成（下游没抢到会话）不算「补过」：那一次会还账并等下一次收场重试，
    // 但同一窗口仍只成功兑现一次（认领成功即清账，见 TryClaimTurn）
    private static bool ShouldCashOwed(GoalModeSession session, DateTimeOffset now)
    {
        if (!session.OwedWake) return false;
        if (session.OwedSince is not { } since) return true;
        return GoalModeWakePolicy.NextWallClockAtOrAfter(session.Rules, since) is { } fire && fire <= now;
    }

    // 「到点撞忙」要不要记一笔（⚠️ 调用方持 _sync）：只有墙钟锚定的规则才有「错过」这回事
    // （间隔类按新的停下重算就行），所以先看一眼从这一次的触发时刻起还有没有墙钟时刻——都没有就干脆不记，
    // 免得一整个忙碌窗口里都挂着「等待下次唤醒」的名不副实的状态
    private static void OweForDue(GoalModeSession session, DateTimeOffset since)
    {
        if (GoalModeWakePolicy.NextWallClockAtOrAfter(session.Rules, since) == null) return;
        Owe(session, since);
    }

    // 记一笔欠账（⚠️ 调用方持 _sync）：since 为 null 表示与墙钟无关，收场必补
    private static void Owe(GoalModeSession session, DateTimeOffset? since)
    {
        if (!session.OwedWake)
        {
            session.OwedWake = true;
            session.OwedSince = since;
            return;
        }

        // 已经欠着了：无条件的那笔更硬（不得被降级），有条件的两笔取更早的起点（窗口更宽）
        if (since == null || session.OwedSince == null) session.OwedSince = null;
        else if (since < session.OwedSince) session.OwedSince = since;
    }

    // 按规则排下次唤醒；没有可触发的规则就保持待命（可手动唤醒或结束）
    private void ScheduleNext(GoalModeSession session, int? idleStreak = null)
    {
        GoalModeScheduledWake? wake;
        CancellationToken token = default;
        lock (_sync)
        {
            if (session.IsEnded) return;
            wake = GoalModeWakePolicy.NextWake(session.Rules, _now(), session.LastStop,
                idleStreak ?? session.BackoffLevel);
            if (wake is { } armed)
            {
                session.ScheduledWakeAt = armed.ScheduledAt;
                token = session.BeginDelay(armed.At);
            }
            else
            {
                session.ScheduledWakeAt = null;
                session.CancelDelay();
            }
        }

        RaiseStatusChanged(session.Session.SessionId);
        if (wake is { } next)
        {
            DelayThenWakeAsync(session, next.At - _now(), token).LogOnFault("wake the goal mode session later");
        }
    }

    private async Task DelayThenWakeAsync(GoalModeSession session, TimeSpan delay, CancellationToken token)
    {
        if (!await WakeLoop.WaitAsync(_delay, delay, token).ConfigureAwait(false)) return;

        EWakeDue due;
        ClaimedTurn? turn;
        DateTimeOffset now = _now();
        lock (_sync)
        {
            // 到点后先问共享的那道闸：那次等待还有效吗、会话此刻闲不闲（忙 = 用户在跑自己那一轮）。
            // 「自己那一轮」不进 busy：本循环跑着时延迟必然已摘（claim 就在那个临界区里），再判一次只是冗余
            due = session.ClaimDue(token, SessionManager.Instance.Running.IsBusy(session.Session.SessionId) && !session.IsRunning);
            turn = due == EWakeDue.Go ? TryClaimTurn(session) : null;
            if (due == EWakeDue.Go && turn == null) due = EWakeDue.Busy; // 认领与占用之间恰好开了一轮
            // 撞上忙：这一次不算发生。忙碌窗口从这条唤醒原本的触发时刻算起（不是到点那一刻，
            // 退避会把两者拉开），要不要补见 ShouldCashOwed。
            // 取舍：退避让「到点」晚于触发时刻时，若那一小段里会话其实是闲的，仍会算成一笔欠账——
            // 宁补不丢（那个墙钟时刻确实还没兑现）；要精确得让运行登记处额外记「忙开始时刻」，不值得
            if (due == EWakeDue.Busy) OweForDue(session, session.ScheduledWakeAt ?? now);
        }

        if (turn is { } claimed) RunClaimedTurn(session, claimed);
        else if (due == EWakeDue.Busy) RaiseStatusChanged(session.Session.SessionId);
    }

    /// <summary>已占到的一轮：叫它需要的几样快照，全在控制器的锁里取</summary>
    /// <param name="Turns">这是第几轮</param>
    /// <param name="Now">占到的时刻</param>
    /// <param name="Chat">会话本体</param>
    /// <param name="Token">本轮的生命周期令牌（结束即取消）</param>
    /// <param name="OwedWake">占下时手上那笔欠账（原值）：没真跑成时要还回去</param>
    /// <param name="OwedSince">同上</param>
    /// <param name="Rules">占下时的规则表（没真跑成时凭它认出「期间改过规则」：那笔旧账就不还了）</param>
    private readonly record struct ClaimedTurn(int Turns, DateTimeOffset Now, ChatSession Chat, CancellationToken Token,
        bool OwedWake, DateTimeOffset? OwedSince, IReadOnlyList<GoalModeScheduleRule> Rules);

    // 占下一轮（⚠️ 调用方持 _sync）：结束、本循环自己那一轮还在收尾、会话被别的轮次占着，都占不到。
    // 占不到时不动任何状态——要不要记「欠一次」由调用方按自己的语义定（见 Wake 与 DelayThenWakeAsync）
    private ClaimedTurn? TryClaimTurn(GoalModeSession session)
    {
        EWakeDue claim = session.IsEnded ? EWakeDue.Cancelled
            : session.IsRunning ? EWakeDue.Busy
            : SessionManager.Instance.Running.IsBusy(session.Session.SessionId) ? EWakeDue.Busy
            : EWakeDue.Go;
        if (claim != EWakeDue.Go) return null;

        ClaimedTurn claimed = new(session.Turns + 1, _now(), session.Session, session.Token,
            session.OwedWake, session.OwedSince, session.Rules); // 清账前先把原值带出去：万一下游没抢到会话，要原样还回来

        session.CancelDelay();
        session.ScheduledWakeAt = null;
        session.OwedWake = false;
        session.OwedSince = null;
        session.IsRunning = true;
        session.Turns++;
        return claimed;
    }

    // 叫一轮（首轮、手动唤醒、补欠下的那次都走这里）：占不到就记成「欠一次」，等这一轮收场补。
    // 这几条都是「这一刻就该跑」的诉求，与规则锚在哪里无关
    private void Wake(GoalModeSession session)
    {
        ClaimedTurn? turn;
        lock (_sync)
        {
            turn = TryClaimTurn(session);
            if (turn == null && !session.IsEnded) Owe(session, null);
        }

        if (turn is { } claimed) RunClaimedTurn(session, claimed);
        else RaiseStatusChanged(session.Session.SessionId);
    }

    // 已占到的一轮：开跑。本轮的注入（首轮是目标注入，其余是唤醒提醒）交给 RunTurnAsync 在
    // 「真抢到这个会话」之后才落——见那里的 onStarted
    private void RunClaimedTurn(GoalModeSession session, ClaimedTurn turn)
    {
        RaiseStatusChanged(turn.Chat.SessionId);
        RunTurnAsync(session, turn).LogOnFault("run a goal mode turn");
    }

    private async Task RunTurnAsync(GoalModeSession session, ClaimedTurn turn)
    {
        string sessionId = turn.Chat.SessionId;
        CancellationToken token = turn.Token;
        bool started = false;
        try
        {
            await _wakeTurn(sessionId, "goal-mode", token, () =>
            {
                // 抢到了才落本轮注入：抢不到（会话被用户那一轮占着）就一句都不写——
                // 这是「撞忙不注入」的最后一截，写在控制器里盖不住下游那一步。
                // 抢到与注入之间刚好被结束的，也不再落（那一轮马上会被取消，写进去只是多一条旁白）
                if (session.IsEnded) return;
                Inject(turn.Chat, turn.Turns == 1
                    ? GoalModeTranscript.KickoffNote(session.Goal, turn.Now)
                    : GoalModeTranscript.WakeNote(session.Goal, turn.Now, turn.Chat.UpdatedAt));
                started = true;
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Warning($"Goal mode turn failed: session={sessionId}: {e.Message}");
        }

        bool ended;
        bool owed;
        lock (_sync)
        {
            if (!started)
            {
                session.Turns--; //没跑成的一轮不算（回执里的轮数也不该算它）
                // 占下时清掉的那笔欠账原样还回来（否则这一次墙钟触发就凭空丢了）；
                // 但期间改过规则就不还了——旧的墙钟时刻已随规则作废（与 Update 清账同口径）
                if (!session.IsEnded && turn.OwedWake && ReferenceEquals(session.Rules, turn.Rules))
                {
                    Owe(session, turn.OwedSince);
                }
            }

            session.IsRunning = false;
            ended = session.IsEnded;
            owed = started && ShouldCashOwed(session, _now());
            // 同 OnTurnEnded：不补才结清，要补就留给 Wake 认领时清（认领落空时还回来）
            if (started && !owed)
            {
                session.OwedWake = false;
                session.OwedSince = null;
            }
        }

        if (!started)
        {
            Log.Debug($"Goal mode turn was not claimed: session={sessionId} (session busy)");
        }

        // 跑着时结束了：Finish 没出回执，停稳后在这里出
        if (ended)
        {
            Emit(session);
            RaiseStatusChanged(sessionId);
            return;
        }

        RaiseStatusChanged(sessionId);
        // 本循环这一轮停稳了：到点撞忙欠下的那次在这里补；否则按规则排下一次（OnTurnEnded 见到
        // IsRunning 就不排，交到这里——两处不会各排一次）
        if (owed) Wake(session);
        else ScheduleNext(session);
    }

    // 标记结束、摘掉登记、落存档；正在跑就等那一轮停稳再出回执（RunTurnAsync 收尾补出）
    private void Finish(GoalModeSession session, EGoalModeEndReason reason, string summary)
    {
        bool running;
        lock (_sync)
        {
            if (session.IsEnded) return;
            session.End(reason, summary);
            session.ScheduledWakeAt = null;
            _sessions.Remove(session.Session.SessionId);
            running = session.IsRunning;
        }

        Save();
        RaiseStatusChanged(session.Session.SessionId);
        if (!running) Emit(session);
    }

    // 出回执：交给界面（统一清单画卡），历史不另写
    private void Emit(GoalModeSession session)
    {
        GoalModeReceipt receipt = new(session.Session.SessionId, session.EndReason!.Value, session.EndSummary,
            session.StartedAt, _now(), session.Turns);
        session.Dispose();
        WakeLoop.Raise(Ended, receipt, "Goal mode ended");
    }

    // 落一条目标模式注入（User 角色 + 旁白标注）：模型读历史看到，UI 画成旁白。
    // 用 SaveAppended 而不是 Save()：Save 不触发 HistoryAppended，打开着的窗口收不到信号，
    // 旁白要等那一轮跑完对账才补出来——用户看到的就是「唤醒的提醒要等轮完才出现」
    private static void Inject(ChatSession session, string text)
    {
        ChatMessage message = session.CreateMessage(ChatRole.User, text);
        ChatMessageAnnotations.MarkGoalModeInjection(message);
        int from = session.History.AddReturningIndex(message);
        session.SaveAppended(from);
    }

    // 从一段历史里认 goal.end：模型经 ExCall 调用，拆开后是内层的 EndGoalMode
    private static GoalModeEndCall? FindEndCall(ChatSession session, int fromIndex)
    {
        // 快照:这一轮可能正往同一份历史里追加,按下标扫活列表会随长度变化抛越界
        ChatMessage[] history = session.History.Snapshot();
        for (int i = fromIndex; i < history.Length; i++)
        {
            foreach (AIContent content in history[i].Contents)
            {
                if (content is FunctionCallContent call && EndGoalModeTool.Read(ExtensionCalls.Unwrap(call)) is { } end)
                    return end;
            }
        }

        return null;
    }

    // 重启后恢复：登记进行中的目标模式，重排下一次唤醒（该触发没触发的，到点立即补跑）
    private void Restore()
    {
        List<GoalModeSession> restored = [];
        bool recentChanged = false;
        foreach (GoalModeState state in GoalModePersistence.Load())
        {
            ChatSession? session = SessionManager.Instance.Load(state.SessionId);
            if (session == null) continue; //会话没了：存档随下一次 Save 自然清掉
            lock (_sync)
            {
                if (_sessions.ContainsKey(state.SessionId)) continue;
                GoalModeSession goal = GoalModeSession.Restore(session, state);
                _sessions[state.SessionId] = goal;
                // 进行中的也同步进最近设置：重启前开始、恢复后停止的目标模式，设置弹窗仍能回填
                _lastSettings[state.SessionId] = new GoalModeSettings(goal.Goal, goal.Rules, goal.AutoApproveApprovals);
                recentChanged = true;
                restored.Add(goal);
            }
        }

        // 最近设置随存档回来：已结束的目标模式重开设置弹窗仍能看到设过的目标与规则（重启也不丢）
        foreach (GoalModeRecentState state in GoalModePersistence.LoadRecent())
        {
            if (SessionManager.Instance.Load(state.SessionId) == null) continue; //会话没了
            lock (_sync)
            {
                if (_lastSettings.ContainsKey(state.SessionId)) continue;
                _lastSettings[state.SessionId] = new GoalModeSettings(state.Goal, state.Rules, state.AutoApproveApprovals);
            }
        }

        if (recentChanged) SaveRecent();

        // 重启后第一轮按有进展排：不继承存档时的退避级数继续放缓。
        // 取舍：欠着的那一次（`OwedWake`）不入档，重启后靠这条重排重新评估——
        // 间隔 / 每天能自己接上，一次性若触发时刻已在重启前过去就真的过不去了（与旧的 OneShot 语义一致）
        foreach (GoalModeSession goal in restored) ScheduleNext(goal, idleStreak: 0);
    }

    private void Save()
    {
        if (!IsOwner) return;
        List<GoalModeState> states;
        lock (_sync) states = _sessions.Values.Select(x => x.ToState()).ToList();
        GoalModePersistence.Save(states);
    }

    private void SaveRecent()
    {
        if (!IsOwner) return;
        List<GoalModeRecentState> states;
        lock (_sync)
        {
            List<string> gone = [];
            states = [];
            foreach (KeyValuePair<string, GoalModeSettings> item in _lastSettings)
            {
                // 会话没了就不保留：下次保存即清（刚开始/进行中的必然在，只有已删会话会被滤掉）
                if (SessionManager.Instance.GetMeta(item.Key) == null)
                {
                    gone.Add(item.Key);
                    continue;
                }

                states.Add(new GoalModeRecentState(item.Key, item.Value.Goal ?? string.Empty,
                    item.Value.Rules ?? [], item.Value.AutoApproveApprovals ?? false));
            }

            foreach (string id in gone) _lastSettings.Remove(id);
        }

        GoalModePersistence.SaveRecent(states);
    }

    private void RaiseStatusChanged(string sessionId) => WakeLoop.Raise(StatusChanged, sessionId, "Goal mode status");
}
