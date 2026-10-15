using UiharuMind.Core.AI.Chat.WakeLoops;

namespace UiharuMind.Core.AI.Chat.Single.GoalMode;

/// <summary>
/// 一次目标模式的可变状态。没有自己的锁：一律在 <see cref="GoalModeController"/> 的锁里读写
/// </summary>
internal sealed class GoalModeSession : WakeLoopSession<EGoalModeEndReason>
{
    /// <summary>开一次目标模式</summary>
    /// <param name="session">会话</param>
    /// <param name="goal">只给模型看的目标</param>
    /// <param name="rules">唤醒调度规则</param>
    /// <param name="startedAt">开始时刻</param>
    /// <param name="artifactStamp">开始时的产物指纹</param>
    public GoalModeSession(ChatSession session, string goal, IReadOnlyList<GoalModeScheduleRule> rules,
        DateTimeOffset startedAt, string artifactStamp) : base(startedAt, artifactStamp)
    {
        Session = session;
        Goal = goal.Trim();
        Rules = rules;
        HistoryStart = session.History.Count;
        LastStop = startedAt;
    }

    /// <summary>会话</summary>
    public ChatSession Session { get; }

    /// <summary>只给模型看的目标</summary>
    public string Goal { get; private set; }

    /// <summary>唤醒调度规则</summary>
    public IReadOnlyList<GoalModeScheduleRule> Rules { get; private set; }

    /// <summary>结束判定只认这个下标之后模型的 goal.end 调用</summary>
    public int HistoryStart { get; }

    /// <summary>已跑轮数</summary>
    public int Turns { get; set; }

    /// <summary>最近一次停下的时刻（Interval / Immediate 规则从它起算）</summary>
    public DateTimeOffset LastStop { get; set; }

    /// <summary>上次在运行登记处看到的状态（StateChanged 里滤掉「没变化的广播」用）</summary>
    public ESessionRunState LastRunState { get; set; } = ESessionRunState.Idle;

    /// <summary>从存档恢复</summary>
    /// <param name="session">会话</param>
    /// <param name="state">存档</param>
    /// <returns>目标模式；只认恢复之后的 goal.end（之前的调用已处理过）</returns>
    public static GoalModeSession Restore(ChatSession session, GoalModeState state) =>
        new(session, state.Goal, state.Rules, state.StartedAt, state.ArtifactStamp)
        {
            Turns = state.Turns,
            BackoffLevel = state.BackoffLevel,
            LastStop = state.LastStop,
        };

    /// <summary>更新目标与规则（下一轮起生效）</summary>
    /// <param name="goal">新目标；null 不改</param>
    /// <param name="rules">新规则；null 不改</param>
    /// <returns>规则变了为 true（调用方据此重排唤醒）</returns>
    public bool Update(string? goal, IReadOnlyList<GoalModeScheduleRule>? rules)
    {
        if (goal != null) Goal = goal.Trim();
        if (rules == null || rules.SequenceEqual(Rules)) return false;
        Rules = rules;
        return true;
    }

    /// <summary>界面用的快照</summary>
    /// <returns>状态</returns>
    public GoalModeStatus Snapshot() => new(Session.SessionId, StartedAt, Turns, IsRunning, WakeAt, Goal, Rules);

    /// <summary>存档形态</summary>
    /// <returns>存档</returns>
    public GoalModeState ToState() =>
        new(Session.SessionId, Goal, Rules, StartedAt, Turns, ArtifactStamp, LastStop, BackoffLevel);
}
