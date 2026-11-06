using UiharuMind.Core.AI.Chat.Single.GoalMode;
using UiharuMind.Core.AI.Chat.WakeLoops;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 后台唤醒循环共享的那道闸（<see cref="WakeLoopSession{TReason}.ClaimDue"/>）：离席与目标模式的
/// 延迟唤醒都问它。盯的是三件事——那次等待还有效吗、会话忙算不算欠、认领之后等待要摘掉。
/// 目标模式侧的整链在 <see cref="GoalModeControllerTests"/>，这里只管闸本身
/// </summary>
public class WakeLoopSessionTests
{
    [Fact]
    public void ValidAndIdle_IsGo_AndTakesThePendingDelay()
    {
        ProbeSession session = Begin();
        Assert.Equal(EWakeDue.Go, session.ClaimDue(session.Token, busy: false));
        Assert.Null(session.WakeAt); //认领即摘：此后没有第二个排程者在等它
    }

    /// <summary>会话正忙（用户在跑自己那一轮）：叫不了，但那条等待也要摘掉——
    /// 补不补由各域自己定，闸只负责「这次不算发生」</summary>
    [Fact]
    public void Busy_IsBusy_AndTakesThePendingDelay()
    {
        ProbeSession session = Begin();
        Assert.Equal(EWakeDue.Busy, session.ClaimDue(session.Token, busy: true));
        Assert.Null(session.WakeAt);
        Assert.False(session.IsRunning); //不动「本循环那一轮在跑」：那是控制器的账
    }

    /// <summary>那次等待已被重新排程取消（令牌作废）：旧的那次一律不算发生</summary>
    [Fact]
    public void SupersededDelay_IsCancelled_AndLeavesTheNewOne()
    {
        ProbeSession session = new();
        CancellationToken stale = session.BeginDelay(DateTimeOffset.UnixEpoch.AddMinutes(1));
        session.BeginDelay(DateTimeOffset.UnixEpoch.AddMinutes(2)); //重新排程：上一条作废
        Assert.Equal(EWakeDue.Cancelled, session.ClaimDue(stale, busy: false));
        Assert.NotNull(session.WakeAt); //新排的那条还在
    }

    [Fact]
    public void EndedLoop_IsCancelled()
    {
        ProbeSession session = Begin();
        session.End(EGoalModeEndReason.Manual, string.Empty);
        Assert.Equal(EWakeDue.Cancelled, session.ClaimDue(session.Token, busy: false));
    }

    private static ProbeSession Begin()
    {
        ProbeSession session = new();
        session.BeginDelay(DateTimeOffset.UnixEpoch.AddMinutes(1));
        return session;
    }

    /// <summary>闸本身不认业务，借目标模式的结束原因枚举落地一个最小的会话</summary>
    private sealed class ProbeSession : WakeLoopSession<EGoalModeEndReason>
    {
        public ProbeSession() : base(DateTimeOffset.UnixEpoch, "0")
        {
        }
    }
}
