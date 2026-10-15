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
    /// 全部规则里最近的下次唤醒时刻；没有可触发的规则为 null（保持待命，可手动唤醒）。
    /// 连续没进展时在调度时刻上再退避一段（有进展即恢复）
    /// </summary>
    /// <param name="rules">调度规则</param>
    /// <param name="now">当前时刻</param>
    /// <param name="lastStop">最近一次停下的时刻</param>
    /// <param name="idleStreak">连续没进展的轮数（0 不退避；1 退避 BackoffOf(0)，依此类推）</param>
    /// <returns>下次唤醒时刻；没有可触发的规则为 null</returns>
    public static DateTimeOffset? NextWake(IReadOnlyList<GoalModeScheduleRule> rules, DateTimeOffset now,
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

        if (scheduled == null) return null;
        return idleStreak <= 0 ? scheduled : scheduled + BackoffOf(idleStreak - 1);
    }

    private static DateTimeOffset NextDaily(TimeOnly time, DateTimeOffset now)
    {
        DateTimeOffset today = new(now.Year, now.Month, now.Day, time.Hour, time.Minute, 0, now.Offset);
        return today > now ? today : today.AddDays(1);
    }
}
