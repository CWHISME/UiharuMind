namespace UiharuMind.Core.AI.Chat.Single.GoalMode;

/// <summary>
/// 目标模式的唤醒调度（与 <see cref="Group.Away.GroupAwayWakePolicy"/> 同一路：纯函数，好单测）。
/// 规则各自算「下次触发时刻」取最近；没进展时在调度时刻上再退避一段——只放缓、不结束
/// （目标模式无保险丝，出路只有模型自判结束或用户手动结束）
/// </summary>
public static class GoalModeWakePolicy
{
    /// <summary>没进展时退避的起始延迟</summary>
    public static readonly TimeSpan DefaultBackoffStart = TimeSpan.FromSeconds(60);

    /// <summary>没进展时退避的封顶</summary>
    public static readonly TimeSpan DefaultBackoffMax = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 这条规则锚在墙钟上吗：每天 / 一次性是（那个时刻过去了就再没有），间隔 / 立即不是（锚在「停下」）。
    ///
    /// 撞上会话忙时，「这一刻该不该补」看的就是它——墙钟锚定的补，其余按新的停下重新起算。
    /// ⚠️ 新增规则种类时必须同步改这里与 <see cref="NextFireOf"/>，以及设置弹窗那一侧的分支表
    /// （<c>GoalModeViewData</c> 的 IsDaily/IsOneShot/IsInterval、Summary、ToRule 的兜底）
    /// 与下拉框顺序（<c>GoalModeSetupWindow.axaml</c>，与枚举序号同序）。漏改不会报错：
    /// 新种类会静默按「不欠」处理
    /// </summary>
    /// <param name="kind">规则种类</param>
    /// <returns>锚在墙钟上为 true</returns>
    public static bool IsWallClock(EGoalScheduleKind kind) =>
        kind is EGoalScheduleKind.Daily or EGoalScheduleKind.OneShot;

    /// <summary>第 n 次没进展（从 0 起）该等多久：逐次翻倍、封顶</summary>
    /// <param name="level">级数</param>
    /// <returns>退避时长</returns>
    public static TimeSpan BackoffOf(int level)
    {
        double factor = Math.Pow(2, Math.Min(level, 30));
        return TimeSpan.FromTicks((long)Math.Min(DefaultBackoffMax.Ticks, DefaultBackoffStart.Ticks * factor));
    }

    /// <summary>
    /// 一条规则的下次触发时刻
    /// </summary>
    /// <param name="rule">规则</param>
    /// <param name="now">当前时刻</param>
    /// <param name="lastStop">最近一次停下的时刻（Interval / Immediate 从它起算）</param>
    /// <returns>下次触发；这条规则不会再触发为 null</returns>
    public static DateTimeOffset? NextFireOf(GoalModeScheduleRule rule, DateTimeOffset now, DateTimeOffset lastStop)
    {
        return rule.Kind switch
        {
            EGoalScheduleKind.Daily when rule.DailyTime is { } t => NextDaily(t, now),
            EGoalScheduleKind.OneShot when rule.FireAt is { } f => f > now ? f : null,
            EGoalScheduleKind.Interval when rule.Interval is { } d => lastStop + d,
            EGoalScheduleKind.Immediate => lastStop,
            _ => null,
        };
    }

    /// <summary>
    /// 全部规则里最近的下次唤醒（时刻含退避，并带上不含退避的原始时刻）；
    /// 没有可触发的规则为 null（保持待命，可手动唤醒）。
    /// 连续没进展时在调度时刻上再退避一段（有进展即恢复）
    /// </summary>
    /// <param name="rules">调度规则</param>
    /// <param name="now">当前时刻</param>
    /// <param name="lastStop">最近一次停下的时刻</param>
    /// <param name="idleStreak">连续没进展的轮数（0 不退避；1 退避 BackoffOf(0)，依此类推）</param>
    /// <returns>下次唤醒；没有可触发的规则为 null</returns>
    public static GoalModeScheduledWake? NextWake(IReadOnlyList<GoalModeScheduleRule> rules, DateTimeOffset now,
        DateTimeOffset lastStop, int idleStreak)
    {
        DateTimeOffset? scheduled = null;
        foreach (GoalModeScheduleRule rule in rules)
        {
            if (NextFireOf(rule, now, lastStop) is { } fire && (scheduled == null || fire < scheduled))
            {
                scheduled = fire;
            }
        }

        if (scheduled is not { } at) return null;
        TimeSpan backoff = idleStreak <= 0 ? TimeSpan.Zero : BackoffOf(idleStreak - 1);
        return new GoalModeScheduledWake(at + backoff, at);
    }

    /// <summary>
    /// 不早于 <paramref name="at"/> 的下一个<b>墙钟</b>触发时刻（只算每天 / 一次性）；没有为 null。
    ///
    /// 「到点撞忙」欠下的一次到收场时用它判：从那个时刻到现在越过了墙钟触发时刻（含那一刻本身），
    /// 就是真错过（那个时刻过去就再没有了），得补；只有间隔类的话按新的停下重新起算就行，不补
    /// </summary>
    /// <param name="rules">调度规则</param>
    /// <param name="at">窗口起点（含）</param>
    /// <returns>下一个墙钟触发时刻；没有为 null</returns>
    public static DateTimeOffset? NextWallClockAtOrAfter(IReadOnlyList<GoalModeScheduleRule> rules, DateTimeOffset at)
    {
        DateTimeOffset? next = null;
        foreach (GoalModeScheduleRule rule in rules)
        {
            DateTimeOffset? fire = rule.Kind switch
            {
                EGoalScheduleKind.Daily when rule.DailyTime is { } time => NextDaily(time, at, inclusive: true),
                EGoalScheduleKind.OneShot when rule.FireAt is { } fireAt => fireAt >= at ? fireAt : null,
                _ => null,
            };
            if (fire is { } moment && (next == null || moment < next)) next = moment;
        }

        return next;
    }

    private static DateTimeOffset NextDaily(TimeOnly time, DateTimeOffset now, bool inclusive = false)
    {
        DateTimeOffset today = new(now.Year, now.Month, now.Day, time.Hour, time.Minute, 0, now.Offset);
        if (inclusive ? today >= now : today > now) return today;
        return today.AddDays(1);
    }
}
