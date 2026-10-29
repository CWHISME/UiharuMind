/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.History;
using Xunit;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 会话上下文预算：模型窗口与会话自设上限合出来的那一个数。
/// 两条口径分开钉——「只收紧不放宽」决定请求会不会超窗，
/// 「下限」决定用户调低之后会不会一设完就当场写一份记忆压缩。
/// </summary>
public class SessionContextBudgetTests
{
    [Fact]
    public void Resolve_NoOverride_FollowsModel()
    {
        Assert.Equal(128_000, SessionContextBudget.Resolve(128_000, 0));
    }

    [Fact]
    public void Resolve_OverrideBelowModel_Wins()
    {
        Assert.Equal(64_000, SessionContextBudget.Resolve(128_000, 64_000));
    }

    /// <summary>
    /// 只能收紧，不能放宽：设到模型窗口之上只会让请求撞上服务端的上限直接失败，
    /// 而「请求超窗」是那一整轮全废，不是少一截历史
    /// </summary>
    [Fact]
    public void Resolve_OverrideAboveModel_IsClamped()
    {
        Assert.Equal(128_000, SessionContextBudget.Resolve(128_000, 256_000));
    }

    /// <summary>模型还没选出来时不夹：宁可让服务端当场报错，也不要静默按更小的窗口把长对话截掉</summary>
    [Fact]
    public void Resolve_ModelUnknown_KeepsOverride()
    {
        Assert.Equal(64_000, SessionContextBudget.Resolve(0, 64_000));
    }

    [Fact]
    public void MinimumFor_NoUsage_IsZero()
    {
        Assert.Equal(0, SessionContextBudget.MinimumFor(0, 128_000));
    }

    /// <summary>
    /// 下限不是「当前占用」，而是「占用 / 交接水位 + 预留」：128k 的模型占用了 60k 时，
    /// 调到 61k 的下一发就会越过交接线，等于一设完就当场写一份记忆压缩
    /// </summary>
    [Fact]
    public void MinimumFor_KeepsOneTurnOfHeadroom()
    {
        int floor = SessionContextBudget.MinimumFor(60_000, 128_000);

        Assert.Equal(84_000, floor); // 60k / 0.8 + 8192 预留，向上取整到 1k
    }

    /// <summary>
    /// 下限要真的挡住「立刻交接」：踩在下限上不触发，再低一档就触发。
    /// 这条比一个数字更耐用——水位比例改了它跟着改，而写死 84000 的那个断言不会
    /// </summary>
    [Fact]
    public void MinimumFor_IsExactlyWhereHandoffStopsFiring()
    {
        const long usage = 60_000;
        int floor = SessionContextBudget.MinimumFor(usage, 128_000);

        Assert.False(HistoryHandoff.ShouldWrite(usage, floor));
        Assert.True(HistoryHandoff.ShouldWrite(usage, floor - 1000));
    }

    /// <summary>占用已经顶到窗口：下限收到窗口上，可调区间为空——那时该做的是清理历史，不是调滑杆</summary>
    [Fact]
    public void MinimumFor_NeverExceedsModelWindow()
    {
        Assert.Equal(128_000, SessionContextBudget.MinimumFor(200_000, 128_000));
    }

    [Fact]
    public void Fits_RejectsBelowMinimum()
    {
        Assert.False(SessionContextBudget.Fits(60_000, 128_000, 61_000));
        Assert.True(SessionContextBudget.Fits(60_000, 128_000, 96_000));
        Assert.False(SessionContextBudget.Fits(60_000, 128_000, 0));
    }
}
