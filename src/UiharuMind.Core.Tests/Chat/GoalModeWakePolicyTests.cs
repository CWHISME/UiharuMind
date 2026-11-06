using UiharuMind.Core.AI.Chat.Single.GoalMode;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 目标模式的唤醒调度（纯函数）：各种规则、多条取最近、没进展退避、OneShot 触发后作废
/// </summary>
public class GoalModeWakePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 10, 9, 0, 0, TimeSpan.Zero); //周日上午九点

    [Fact]
    public void Daily_LaterToday_IsTodayAtTheTime()
    {
        GoalModeScheduleRule rule = new(EGoalScheduleKind.Daily, DailyTime: new TimeOnly(14, 30));
        Assert.Equal(new DateTimeOffset(2026, 5, 10, 14, 30, 0, TimeSpan.Zero),
            GoalModeWakePolicy.NextFireOf(rule, Now, Now));
    }

    [Fact]
    public void Daily_AlreadyPassed_IsTomorrowAtTheTime()
    {
        GoalModeScheduleRule rule = new(EGoalScheduleKind.Daily, DailyTime: new TimeOnly(8, 0));
        Assert.Equal(new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero),
            GoalModeWakePolicy.NextFireOf(rule, Now, Now));
    }

    [Fact]
    public void OneShot_InTheFuture_IsTheFireAt()
    {
        DateTimeOffset fireAt = Now.AddHours(3);
        GoalModeScheduleRule rule = new(EGoalScheduleKind.OneShot, FireAt: fireAt);
        Assert.Equal(fireAt, GoalModeWakePolicy.NextFireOf(rule, Now, Now));
    }

    [Fact]
    public void OneShot_InThePast_IsNull()
    {
        GoalModeScheduleRule rule = new(EGoalScheduleKind.OneShot, FireAt: Now.AddHours(-1));
        Assert.Null(GoalModeWakePolicy.NextFireOf(rule, Now, Now));
    }

    [Fact]
    public void Interval_CountsFromLastStop()
    {
        GoalModeScheduleRule rule = new(EGoalScheduleKind.Interval, Interval: TimeSpan.FromMinutes(20));
        DateTimeOffset lastStop = Now.AddMinutes(-5);
        Assert.Equal(lastStop + TimeSpan.FromMinutes(20),
            GoalModeWakePolicy.NextFireOf(rule, Now, lastStop));
    }

    [Fact]
    public void MultipleRules_TakesTheNearest()
    {
        GoalModeScheduleRule interval = new(EGoalScheduleKind.Interval, Interval: TimeSpan.FromHours(1));
        GoalModeScheduleRule daily = new(EGoalScheduleKind.Daily, DailyTime: new TimeOnly(9, 30)); //九点半：最近
        GoalModeScheduleRule oneShot = new(EGoalScheduleKind.OneShot, FireAt: Now.AddHours(5));

        Assert.Equal(new DateTimeOffset(2026, 5, 10, 9, 30, 0, TimeSpan.Zero),
            GoalModeWakePolicy.NextWake([interval, daily, oneShot], Now, Now, idleStreak: 0)?.At);
    }

    [Fact]
    public void NoRules_ReturnsNull()
    {
        Assert.Null(GoalModeWakePolicy.NextWake([], Now, Now, idleStreak: 0));
    }

    [Fact]
    public void AllOneShotsExpired_ReturnsNull()
    {
        GoalModeScheduleRule expired = new(EGoalScheduleKind.OneShot, FireAt: Now.AddHours(-1));
        Assert.Null(GoalModeWakePolicy.NextWake([expired], Now, Now, idleStreak: 0));
    }

    /// <summary>退避只推后实际到点时刻，不动「原本的触发时刻」——后者是撞忙时判「越过了哪一刻」的窗口起点</summary>
    [Fact]
    public void NoProgress_DelaysTheArmedMoment_ButKeepsTheScheduledOne()
    {
        GoalModeScheduleRule interval = new(EGoalScheduleKind.Interval, Interval: TimeSpan.FromMinutes(30));
        GoalModeScheduledWake scheduled = Assert.IsType<GoalModeScheduledWake>(
            GoalModeWakePolicy.NextWake([interval], Now, Now, idleStreak: 1));
        Assert.Equal(Now + TimeSpan.FromMinutes(30), scheduled.ScheduledAt);
        Assert.Equal(Now + TimeSpan.FromMinutes(30) + GoalModeWakePolicy.DefaultBackoffStart, scheduled.At);
    }

    [Fact]
    public void Immediate_FiresAtLastStop()
    {
        GoalModeScheduleRule rule = new(EGoalScheduleKind.Immediate);
        DateTimeOffset lastStop = Now.AddMinutes(-1);
        Assert.Equal(lastStop, GoalModeWakePolicy.NextFireOf(rule, Now, lastStop));
    }

    [Fact]
    public void LongerIdleStreak_BacksOffFurther()
    {
        GoalModeScheduleRule immediate = new(EGoalScheduleKind.Immediate);
        Assert.Equal(Now + GoalModeWakePolicy.BackoffOf(2),
            GoalModeWakePolicy.NextWake([immediate], Now, Now, idleStreak: 3)?.At);
    }

    /// <summary>锚点由规则种类决定：每天 / 一次性是墙钟，间隔 / 立即是「停下」</summary>
    [Fact]
    public void IsWallClock_OnlyForDailyAndOneShot()
    {
        Assert.True(GoalModeWakePolicy.IsWallClock(EGoalScheduleKind.Daily));
        Assert.True(GoalModeWakePolicy.IsWallClock(EGoalScheduleKind.OneShot));
        Assert.False(GoalModeWakePolicy.IsWallClock(EGoalScheduleKind.Interval));
        Assert.False(GoalModeWakePolicy.IsWallClock(EGoalScheduleKind.Immediate));
    }

    /// <summary>撞忙窗口里被越过的墙钟时刻：含窗口起点本身（那一次就是它自己到点）</summary>
    [Fact]
    public void NextWallClockAtOrAfter_IncludesTheWindowStart()
    {
        GoalModeScheduleRule daily = new(EGoalScheduleKind.Daily, DailyTime: new TimeOnly(9, 30));
        Assert.Equal(new DateTimeOffset(2026, 5, 10, 9, 30, 0, TimeSpan.Zero),
            GoalModeWakePolicy.NextWallClockAtOrAfter([daily], new DateTimeOffset(2026, 5, 10, 9, 30, 0, TimeSpan.Zero)));
        // 窗口起点越过今天的时刻：下一个就是明天
        Assert.Equal(new DateTimeOffset(2026, 5, 11, 9, 30, 0, TimeSpan.Zero),
            GoalModeWakePolicy.NextWallClockAtOrAfter([daily], new DateTimeOffset(2026, 5, 10, 9, 31, 0, TimeSpan.Zero)));
    }

    /// <summary>间隔类不算墙钟：只有它们时，撞忙窗口里永远没有可补的时刻</summary>
    [Fact]
    public void NextWallClockAtOrAfter_IgnoresIntervalAndExpiredOneShot()
    {
        GoalModeScheduleRule interval = new(EGoalScheduleKind.Interval, Interval: TimeSpan.FromMinutes(30));
        GoalModeScheduleRule expired = new(EGoalScheduleKind.OneShot, FireAt: Now.AddMinutes(-1));
        Assert.Null(GoalModeWakePolicy.NextWallClockAtOrAfter([interval, expired], Now));
    }

    /// <summary>混排：间隔先到点也不算这一次就完了，墙钟那一条仍然会被看到</summary>
    [Fact]
    public void NextWallClockAtOrAfter_SeesTheLaterWallClockRule()
    {
        GoalModeScheduleRule interval = new(EGoalScheduleKind.Interval, Interval: TimeSpan.FromMinutes(30));
        GoalModeScheduleRule oneShot = new(EGoalScheduleKind.OneShot, FireAt: Now.AddMinutes(5));
        Assert.Equal(Now.AddMinutes(5), GoalModeWakePolicy.NextWallClockAtOrAfter([interval, oneShot], Now));
    }

    [Fact]
    public void BackoffOf_DoublesEachLevel_AndCaps()
    {
        Assert.Equal(GoalModeWakePolicy.DefaultBackoffStart, GoalModeWakePolicy.BackoffOf(0));
        Assert.Equal(GoalModeWakePolicy.DefaultBackoffStart * 2, GoalModeWakePolicy.BackoffOf(1));
        Assert.Equal(GoalModeWakePolicy.DefaultBackoffStart * 4, GoalModeWakePolicy.BackoffOf(2));
        // 高级数封顶在最大值，不溢出
        Assert.Equal(GoalModeWakePolicy.DefaultBackoffMax, GoalModeWakePolicy.BackoffOf(100));
    }
}
