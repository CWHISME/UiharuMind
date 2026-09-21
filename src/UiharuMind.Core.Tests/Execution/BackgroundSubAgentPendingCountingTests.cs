using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 未交回轮次的记账口径：同一子会话可以排队多轮（主代理插话回落续跑），
/// 「数量统计」按<b>轮次</b>计而不是按 id 计——否则先交回的那一轮会把
/// 还在跑的后续轮次一起摘掉（计数提前归零、右栏显示「没有在跑」）。
/// </summary>
public class BackgroundSubAgentPendingCountingTests
{
    [Fact]
    public void SameSubSession_WithQueuedRounds_CountsRoundsNotIds()
    {
        // 同一子会话先派轮1、再排队轮2
        BackgroundSubAgentDispatcher.AddPendingTurn("count-parent", "count-sub1");
        BackgroundSubAgentDispatcher.AddPendingTurn("count-parent", "count-sub1");

        Assert.Equal(2, BackgroundSubAgentDispatcher.PendingCount("count-parent"));
        Assert.True(BackgroundSubAgentDispatcher.IsAwaitingReport("count-sub1"));

        // 轮1交回：还剩轮2，计数是 1 而不是 0
        BackgroundSubAgentDispatcher.RemovePendingTurn("count-parent", "count-sub1");
        Assert.Equal(1, BackgroundSubAgentDispatcher.PendingCount("count-parent"));
        Assert.True(BackgroundSubAgentDispatcher.IsAwaitingReport("count-sub1"));

        // 轮2交回：彻底清零
        BackgroundSubAgentDispatcher.RemovePendingTurn("count-parent", "count-sub1");
        Assert.Equal(0, BackgroundSubAgentDispatcher.PendingCount("count-parent"));
        Assert.False(BackgroundSubAgentDispatcher.IsAwaitingReport("count-sub1"));
    }

    [Fact]
    public void DifferentSubSessions_CountPerParent()
    {
        BackgroundSubAgentDispatcher.AddPendingTurn("count-parent-a", "count-a1");
        BackgroundSubAgentDispatcher.AddPendingTurn("count-parent-a", "count-a2");
        BackgroundSubAgentDispatcher.AddPendingTurn("count-parent-b", "count-b1");

        Assert.Equal(2, BackgroundSubAgentDispatcher.PendingCount("count-parent-a"));
        Assert.Equal(1, BackgroundSubAgentDispatcher.PendingCount("count-parent-b"));
        Assert.True(BackgroundSubAgentDispatcher.IsAwaitingReport("count-a2"));
        Assert.False(BackgroundSubAgentDispatcher.IsAwaitingReport("count-x"));

        BackgroundSubAgentDispatcher.RemovePendingTurn("count-parent-a", "count-a1");
        BackgroundSubAgentDispatcher.RemovePendingTurn("count-parent-a", "count-a2");
        BackgroundSubAgentDispatcher.RemovePendingTurn("count-parent-b", "count-b1");
    }

    [Fact]
    public void EmptyOrMissingIds_AreSafe()
    {
        Assert.Equal(0, BackgroundSubAgentDispatcher.PendingCount(null));
        Assert.Equal(0, BackgroundSubAgentDispatcher.PendingCount(string.Empty));
        Assert.False(BackgroundSubAgentDispatcher.IsAwaitingReport(null));
        Assert.False(BackgroundSubAgentDispatcher.IsAwaitingReport(string.Empty));
    }

    /// <summary>
    /// 同一子会话排队两轮时，先交回的那轮<b>不能</b>把 id 从「按 id 追踪」集合摘掉
    /// （否则三档显示对还在跑的排队轮隐身，与轮次计数同源的 id 级 bug）。
    /// </summary>
    [Fact]
    public void QueuedRound_KeepsTheIdTrackedUntilAllRoundsReturn()
    {
        string parent = "count-track";
        string sub = "count-track-sub";
        BackgroundSubAgentDispatcher.AddPendingTurn(parent, sub);
        BackgroundSubAgentDispatcher.AddPendingTurn(parent, sub);

        // 轮1 交回:id 仍被追踪（轮2 还在）
        BackgroundSubAgentDispatcher.RemovePendingTurn(parent, sub);
        Assert.True(BackgroundSubAgentDispatcher.IsSubSessionTracked(parent, sub));

        // 轮2 交回:追踪摘掉
        BackgroundSubAgentDispatcher.RemovePendingTurn(parent, sub);
        Assert.False(BackgroundSubAgentDispatcher.IsSubSessionTracked(parent, sub));
    }

    /// <summary>
    /// 父级计数<b>只减不摘</b>：不同子会话对同一父计数递减时，若归零后把键 TryRemove 掉，
    /// 恰逢新一轮派发（Add）会被连带摘掉（不同 sub 持不同 roundLock，盖不到的竞态）。
    /// 键保留 0 值：读 0 无害，且新一轮能正常 +1。
    /// </summary>
    [Fact]
    public void ParentCount_ZeroedByOneSub_StillReadableAndReusableByAnother()
    {
        string parent = "count-parent-x";
        BackgroundSubAgentDispatcher.AddPendingTurn(parent, "count-x1");
        BackgroundSubAgentDispatcher.AddPendingTurn(parent, "count-x2");

        BackgroundSubAgentDispatcher.RemovePendingTurn(parent, "count-x1");
        BackgroundSubAgentDispatcher.RemovePendingTurn(parent, "count-x2");
        Assert.Equal(0, BackgroundSubAgentDispatcher.PendingCount(parent));

        // 键未被摘掉:新一轮派发能正常 +1(而不是被 TryRemove 掉之后 Add 凭空消失)
        BackgroundSubAgentDispatcher.AddPendingTurn(parent, "count-x3");
        Assert.Equal(1, BackgroundSubAgentDispatcher.PendingCount(parent));
    }
}