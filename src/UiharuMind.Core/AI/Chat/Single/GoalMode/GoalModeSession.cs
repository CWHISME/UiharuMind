using UiharuMind.Core.AI.Chat.WakeLoops;

namespace UiharuMind.Core.AI.Chat.Single.GoalMode;

/// <summary>
/// 一次目标模式的可变状态。没有自己的锁：一律在 <see cref="GoalModeController"/> 的锁里读写
/// </summary>
internal sealed class GoalModeSession : WakeLoopSession<EGoalModeEndReason>
{
    /// <summary>开一次目标模式</summary>
    /// <param name="session">会话</param>
    /// <param name="goal">只给模型看的目标；null 当空串（规则里 null 是「不改」，目标没有这一说）</param>
    /// <param name="rules">唤醒调度规则</param>
    /// <param name="startedAt">开始时刻</param>
    /// <param name="artifactStamp">开始时的产物指纹</param>
    /// <param name="autoApproveApprovals">唤醒轮的审批自动同意（无人值守也直接放行）</param>
    public GoalModeSession(ChatSession session, string? goal, IReadOnlyList<GoalModeScheduleRule> rules,
        DateTimeOffset startedAt, string artifactStamp, bool autoApproveApprovals = false) : base(startedAt, artifactStamp)
    {
        Session = session;
        Goal = goal?.Trim() ?? string.Empty;
        Rules = rules;
        AutoApproveApprovals = autoApproveApprovals;
        HistoryStart = session.History.Count;
        LastStop = startedAt;
        // 登记时按登记处此刻的状态起算：开始目标模式时这个会话已经跑着（用户在跑自己那一轮）的话，
        // 那一轮的收场广播才不会被当成「没变化的广播」滤掉，欠下的那次唤醒也就补得上。
        // ⚠️ 这里读 SessionManager 单例：初始化链是 GoalModeController ctor → SessionManager.Instance 在前，
        // 不构成回环；但别在 SessionManager.OnInitialize 里引 GoalModeController.Instance，那才成环
        LastRunState = SessionManager.Instance.Running.StateOf(session.SessionId);
    }

    /// <summary>会话</summary>
    public ChatSession Session { get; }

    /// <summary>只给模型看的目标</summary>
    public string Goal { get; private set; }

    /// <summary>唤醒调度规则</summary>
    public IReadOnlyList<GoalModeScheduleRule> Rules { get; private set; }

    /// <summary>唤醒轮的审批是否自动同意（无人值守时也直接放行，用户在设置弹窗确认过危险度）</summary>
    public bool AutoApproveApprovals { get; private set; }

    /// <summary>结束判定只认这个下标之后模型的 goal.end 调用</summary>
    public int HistoryStart { get; }

    /// <summary>已跑轮数</summary>
    public int Turns { get; set; }

    /// <summary>最近一次停下的时刻（Interval / Immediate 规则从它起算）</summary>
    public DateTimeOffset LastStop { get; set; }

    /// <summary>上次在运行登记处看到的状态（StateChanged 里滤掉「没变化的广播」用）</summary>
    public ESessionRunState LastRunState { get; set; } = ESessionRunState.Idle;

    /// <summary>
    /// 排着的那次唤醒原本的触发时刻（不含退避）；没在等为 null。
    /// 到点撞忙时拿它当忙碌窗口的起点——退避会把实际到点时刻推到它之后，用后者会漏掉那一刻
    /// </summary>
    public DateTimeOffset? ScheduledWakeAt { get; set; }

    /// <summary>
    /// 有一轮该跑而没跑成：首轮还没跑（`Start` 撞上会话正忙）、用户按了「立即唤醒」（`WakeNow` 撞忙），
    /// 以及「到点撞忙」的那一次。欠着期间不注入提醒、不计轮数；这一轮收场时补。
    ///
    /// 与群侧 <c>GroupAwaySession.WakePending</c> 同名异义：那一个是「化身跑着时又有一波收场」的排队旗，
    /// 由波末重判去消费；这里只是单聊目标模式的欠账，消费点在本控制器
    /// </summary>
    public bool OwedWake { get; set; }

    /// <summary>
    /// 「到点撞忙」那一次的时刻；null 表示欠的这笔与墙钟无关（首轮 / 手动唤醒 —— 无条件补）。
    /// 有值时要到收场时再问一句：从这一刻到现在，有没有越过某条墙钟规则（每天 / 一次性）的触发时刻——
    /// 越过了才补（那个时刻过去就再没有了）；只有间隔类的话按新的停下重新起算就行，不补
    /// </summary>
    public DateTimeOffset? OwedSince { get; set; }

    /// <summary>从存档恢复</summary>
    /// <param name="session">会话</param>
    /// <param name="state">存档</param>
    /// <returns>目标模式；只认恢复之后的 goal.end（之前的调用已处理过）</returns>
    public static GoalModeSession Restore(ChatSession session, GoalModeState state) =>
        new(session, state.Goal, state.Rules, state.StartedAt, state.ArtifactStamp, state.AutoApproveApprovals)
        {
            Turns = state.Turns,
            BackoffLevel = state.BackoffLevel,
            LastStop = state.LastStop,
        };

    /// <summary>更新目标、规则与自动同意：目标与规则下一轮起生效，自动同意下一批就生效（每批现问）</summary>
    /// <param name="goal">新目标；null 不改</param>
    /// <param name="rules">新规则；null 不改</param>
    /// <param name="autoApproveApprovals">自动同意审批的新值；null 不改</param>
    /// <returns>
    /// 规则变了为 true（调用方据此重排唤醒）。
    /// <b>实例稳定性是个约定</b>：内容没变就不换实例，变了才换——控制器靠
    /// <c>ReferenceEquals(session.Rules, 占轮时的快照)</c> 认出「期间改过规则」（见其还账路径），
    /// 改成「先换实例再比值」会让那里静默失效
    /// </returns>
    public bool Update(string? goal, IReadOnlyList<GoalModeScheduleRule>? rules, bool? autoApproveApprovals)
    {
        if (goal != null) Goal = goal.Trim();
        if (autoApproveApprovals.HasValue) AutoApproveApprovals = autoApproveApprovals.Value;
        if (rules == null || rules.SequenceEqual(Rules)) return false;
        Rules = rules;
        return true;
    }

    /// <summary>界面用的快照</summary>
    /// <returns>状态</returns>
    public GoalModeStatus Snapshot() =>
        new(Session.SessionId, StartedAt, Turns, IsRunning, WakeAt, Goal, Rules, AutoApproveApprovals, OwedWake);

    /// <summary>存档形态</summary>
    /// <returns>存档</returns>
    public GoalModeState ToState() =>
        new(Session.SessionId, Goal, Rules, StartedAt, Turns, ArtifactStamp, LastStop, BackoffLevel,
            AutoApproveApprovals);
}
