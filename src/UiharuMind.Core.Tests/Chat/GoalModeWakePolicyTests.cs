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

        DateTimeOffset? next = GoalModeWakePolicy.NextWake([interval, daily, oneShot], Now, Now, idleStreak: 0);
        Assert.Equal(new DateTimeOffset(2026, 5, 10, 9, 30, 0, TimeSpan.Zero), next);
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

    [Fact]
    public void NoProgress_DelaysTheScheduledWakeByBackoff()
    {
        GoalModeScheduleRule interval = new(EGoalScheduleKind.Interval, Interval: TimeSpan.FromMinutes(30));
        DateTimeOffset scheduled = Now + TimeSpan.FromMinutes(30);
        Assert.Equal(scheduled + GoalModeWakePolicy.DefaultBackoffStart,
            GoalModeWakePolicy.NextWake([interval], Now, Now, idleStreak: 1));
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
            GoalModeWakePolicy.NextWake([immediate], Now, Now, idleStreak: 3));
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
