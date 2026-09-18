using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 唤醒合并窗口的并发去重：同一父会话同时只允许一个「唤醒决定」（先到者等阈值、窗口期内
/// 到达的报告并入那一轮），窗口结束后新的完成者可以再开。不测时间本身（60s 窗口放真实的
/// Task.Delay 跑测试太慢），只钉住「去重 + 可重入 + 按父会话隔离」三条不变量。
/// </summary>
public class BackgroundSubAgentWakeMergeTests
{
    [Fact]
    public void ConcurrentWakes_OnlyOneHoldsTheGate()
    {
        // 三份报告同一瞬间交回：只有第一个能开窗口，其余被并入
        int holders = 0;
        for (int i = 0; i < 3; i++)
        {
            if (BackgroundSubAgentDispatcher.BeginWakeMergeWindow("parent-1")) holders++;
        }

        Assert.Equal(1, holders);

        BackgroundSubAgentDispatcher.EndWakeMergeWindow("parent-1");
    }

    [Fact]
    public void Gate_ReopensAfterTheWindowEnds()
    {
        Assert.True(BackgroundSubAgentDispatcher.BeginWakeMergeWindow("parent-1"));
        BackgroundSubAgentDispatcher.EndWakeMergeWindow("parent-1");

        Assert.True(BackgroundSubAgentDispatcher.BeginWakeMergeWindow("parent-1"));
        BackgroundSubAgentDispatcher.EndWakeMergeWindow("parent-1");
    }

    [Fact]
    public void Gate_IsPerParent()
    {
        BackgroundSubAgentDispatcher.BeginWakeMergeWindow("parent-a");
        try
        {
            Assert.True(BackgroundSubAgentDispatcher.BeginWakeMergeWindow("parent-b"));
        }
        finally
        {
            BackgroundSubAgentDispatcher.EndWakeMergeWindow("parent-a");
            BackgroundSubAgentDispatcher.EndWakeMergeWindow("parent-b");
        }
    }

    [Fact]
    public void EmptyParentId_NeverHoldsTheGate()
    {
        Assert.False(BackgroundSubAgentDispatcher.BeginWakeMergeWindow(null));
        Assert.False(BackgroundSubAgentDispatcher.BeginWakeMergeWindow(string.Empty));
    }

    /// <summary>跳过判据边界：用户轮必须严格晚于最后一份报告落盘才算读过它</summary>
    [Fact]
    public void ShouldSkipWake_RequiresUserTurnStrictlyAfterTheLastReport()
    {
        DateTime t = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(BackgroundSubAgentDispatcher.ShouldSkipWake(t.AddSeconds(1), t));
        Assert.False(BackgroundSubAgentDispatcher.ShouldSkipWake(t, t));
        Assert.False(BackgroundSubAgentDispatcher.ShouldSkipWake(t.AddSeconds(-1), t));
    }

    /// <summary>
    /// M2 轮询的决策纯函数：还有未交回轮次且未到封顶时刻才继续等；pending 归零即醒、
    /// 到 1h 封顶必醒。这条钉住「实机症状（两代理 done+Appended 却无唤醒）」的根因修复。
    /// </summary>
    [Fact]
    public void ShouldKeepWaiting_StopsWhenPendingClearsOrDeadlineHits()
    {
        DateTime now = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        DateTime future = now.AddHours(1);

        // 还有未交回、还没到封顶 → 继续等
        Assert.True(BackgroundSubAgentDispatcher.ShouldKeepWaiting(1, now, future));
        // pending 归零 → 提前醒
        Assert.False(BackgroundSubAgentDispatcher.ShouldKeepWaiting(0, now, future));
        // 到封顶时刻仍有未交回 → 必醒
        Assert.False(BackgroundSubAgentDispatcher.ShouldKeepWaiting(1, future, future));
        Assert.False(BackgroundSubAgentDispatcher.ShouldKeepWaiting(1, future.AddSeconds(1), future));
    }

    /// <summary>
    /// 用户轮发生在最后一份报告落盘<b>前</b>：那份报告没被读过，不应跳过唤醒。
    /// 只测这一个方向——反向（先落盘后说话）依赖真实时钟毫秒差：同刻也判 false，
    /// 正是判据语义（严格大于），所以两个方向都不会 flaky。
    /// </summary>
    [Fact]
    public void UserTurnBeforeTheLastReport_DoesNotSkipWake()
    {
        string parent = "parent-skip-1";
        BackgroundSubAgentDispatcher.NoteUserTurn(parent);
        BackgroundSubAgentDispatcher.MarkReportPersisted(parent);

        Assert.False(BackgroundSubAgentDispatcher.ShouldSkipWakeForParent(parent));
    }
}