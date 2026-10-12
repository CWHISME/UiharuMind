using UiharuMind.Core.AI.Chat.Group.Away;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>波末唤醒决策表：优先级 保险丝 ＞ 用户停止 ＞ 等后台 ＞ 空推退避 ＞ 立刻唤醒</summary>
public class GroupAwayWakePolicyTests
{
    [Theory]
    [InlineData(true, false, false, false, EGroupAwayWake.FinishIdle)]
    [InlineData(true, true, true, true, EGroupAwayWake.FinishIdle)] //保险丝压倒一切
    [InlineData(false, true, false, true, EGroupAwayWake.WaitStop)] //用户停止优先于等后台
    [InlineData(false, true, true, false, EGroupAwayWake.WaitStop)] //用户停止优先于空推
    [InlineData(false, false, true, true, EGroupAwayWake.DeferBackground)] //等后台优先于空推
    [InlineData(false, false, false, true, EGroupAwayWake.DeferBackground)]
    [InlineData(false, false, true, false, EGroupAwayWake.BackoffEmpty)]
    [InlineData(false, false, false, false, EGroupAwayWake.WakeNow)]
    public void Decide_Priority(bool idleFuse, bool stopped, bool emptyPush, bool hasBackgroundWork,
        EGroupAwayWake expected)
    {
        Assert.Equal(expected, GroupAwayWakePolicy.Decide(idleFuse, stopped, emptyPush, hasBackgroundWork));
    }
}
