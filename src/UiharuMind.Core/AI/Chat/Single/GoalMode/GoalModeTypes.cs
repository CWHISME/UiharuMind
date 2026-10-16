namespace UiharuMind.Core.AI.Chat.Single.GoalMode;

/// <summary>目标模式为什么结束</summary>
public enum EGoalModeEndReason
{
    /// <summary>模型判定目标达成（调 goal.end 且 reason=done）</summary>
    Done,

    /// <summary>模型碰到必须用户本人定的事（调 goal.end 且 reason=needs_user）</summary>
    NeedsUser,

    /// <summary>用户手动结束</summary>
    Manual,
}

/// <summary>唤醒调度规则的种类（序号即设置弹窗下拉框的顺序）</summary>
public enum EGoalScheduleKind
{
    /// <summary>每天指定时刻唤醒一次</summary>
    Daily,

    /// <summary>指定日期时刻唤醒一次，触发后作废</summary>
    OneShot,

    /// <summary>每次停下后隔多久再唤醒（循环）</summary>
    Interval,

    /// <summary>每次停下后立即唤醒</summary>
    Immediate,
}

/// <summary>一条唤醒调度规则（序列化存档；可空字段按 <see cref="Kind"/> 取用）</summary>
public sealed record GoalModeScheduleRule(EGoalScheduleKind Kind, TimeOnly? DailyTime = null,
    DateTimeOffset? FireAt = null, TimeSpan? Interval = null)
{
    /// <summary>没给规则时的默认循环：每 30 分钟，免得开了就停</summary>
    public static GoalModeScheduleRule Default { get; } = new(EGoalScheduleKind.Interval, Interval: TimeSpan.FromMinutes(30));
}

/// <summary>目标模式的参数：设置弹窗里能改的几样，一次交给控制器</summary>
/// <param name="Goal">只给模型看的目标；<see cref="GoalModeController.Update"/> 里为 null 表示不改</param>
/// <param name="Rules">唤醒调度规则；null 不改，空用 <see cref="GoalModeScheduleRule.Default"/></param>
/// <param name="AutoApproveApprovals">唤醒轮的审批自动同意（界面勾选并确认过危险度）；null 不改，缺省不自动同意</param>
public sealed record GoalModeSettings(string? Goal = null, IReadOnlyList<GoalModeScheduleRule>? Rules = null,
    bool? AutoApproveApprovals = null);

/// <summary>目标模式此刻的样子（界面用）</summary>
/// <param name="AutoApproveApprovals">唤醒轮的审批是否自动同意（勾选并确认过危险度才为 true）</param>
public sealed record GoalModeStatus(string SessionId, DateTimeOffset StartedAt, int Turns, bool IsRunning,
    DateTimeOffset? WakeAt, string Goal, IReadOnlyList<GoalModeScheduleRule> Rules, bool AutoApproveApprovals);

/// <summary>一次目标模式结束时的回执原料（界面画成一张只给人看的卡）</summary>
public sealed record GoalModeReceipt(string SessionId, EGoalModeEndReason Reason, string Summary,
    DateTimeOffset StartedAt, DateTimeOffset EndedAt, int Turns);
