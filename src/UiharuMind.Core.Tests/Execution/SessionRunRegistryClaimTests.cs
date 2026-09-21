/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 原子占用：唤醒轮靠它给用户让路。
///
/// 钉住的是「查一下忙不忙，不忙就开跑」那个两步写法的竞态——查与开之间用户正好发一条，
/// 两轮就并行跑在同一个会话本体、执行者与转录器上。
/// </summary>
public class SessionRunRegistryClaimTests
{
    private const string SessionId = "s1";

    [Fact]
    public void TryBeginRun_SucceedsWhenIdle_AndMarksBusy()
    {
        SessionRunRegistry registry = new();

        using IDisposable? claim = registry.TryBeginRun(SessionId);

        Assert.NotNull(claim);
        Assert.Equal(ESessionRunState.Running, registry.StateOf(SessionId));
    }

    [Fact]
    public void TryBeginRun_FailsWhileAnotherRunIsActive()
    {
        SessionRunRegistry registry = new();
        using IDisposable running = registry.BeginRun(SessionId);

        Assert.Null(registry.TryBeginRun(SessionId));
    }

    [Fact]
    public void TryBeginRun_FailsWhileAwaitingApproval()
    {
        //卡在审批上也是「有轮次没结束」,此刻插一轮进来同样会撞
        SessionRunRegistry registry = new();
        using IDisposable running = registry.BeginRun(SessionId);
        using IDisposable waiting = registry.BeginApprovalWait(SessionId);

        Assert.Null(registry.TryBeginRun(SessionId));
    }

    [Fact]
    public void TryBeginRun_ReleasesOnDispose()
    {
        //占用必须还得回去,否则这个会话永久停在「在跑」上,删除与清空历史也跟着被拦死
        SessionRunRegistry registry = new();
        registry.TryBeginRun(SessionId)!.Dispose();

        Assert.Equal(ESessionRunState.Idle, registry.StateOf(SessionId));
        Assert.NotNull(registry.TryBeginRun(SessionId));
    }
}
