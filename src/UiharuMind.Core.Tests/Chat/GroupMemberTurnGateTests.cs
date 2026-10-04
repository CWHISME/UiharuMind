using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 成员轮次闸门（ADR 0063）：私聊抢占群轮、叫停后的闸归私聊、接回在放闸时做
/// </summary>
public class GroupMemberTurnGateTests
{
    private readonly string _member = Guid.NewGuid().ToString(); //闸按会话标识全局共享：每个测试一个新的

    [Fact]
    public async Task PrivateEntry_PreemptsTheGroupTurn_AndGetsTheGateWhenItLetsGo()
    {
        PreemptibleTurn group = GroupMemberTurnGate.TryEnterPreemptible(_member, CancellationToken.None)!;

        Task<IDisposable> privateTurn = GroupMemberTurnGate.EnterPrivateAsync(_member);

        Assert.True(group.Token.IsCancellationRequested);
        Assert.False(privateTurn.IsCompleted);
        Assert.True(group.WasPreempted(completed: false));

        group.Dispose();
        // 私聊排在前面：放闸就归它，同时醒来的接回抢不走
        Assert.Null(GroupMemberTurnGate.TryEnterPreemptible(_member, CancellationToken.None));
        (await privateTurn).Dispose();
    }

    /// <summary>跑完之后才到的叫停、或同时停了群，都不算被私聊叫停</summary>
    [Fact]
    public async Task WasPreempted_NotWhenCompleted_NorWhenTheGroupWasStopped()
    {
        using CancellationTokenSource groupStop = new();
        PreemptibleTurn group = GroupMemberTurnGate.TryEnterPreemptible(_member, groupStop.Token)!;
        Task<IDisposable> privateTurn = GroupMemberTurnGate.EnterPrivateAsync(_member);

        Assert.False(group.WasPreempted(completed: true));
        groupStop.Cancel();
        Assert.False(group.WasPreempted(completed: false));

        group.Dispose();
        (await privateTurn).Dispose();
    }

    [Fact]
    public async Task ResumeAfter_RunsWhenTheGateIsReleased_OrRightAwayIfFree()
    {
        IDisposable held = GroupMemberTurnGate.TryEnter(_member)!;
        TaskCompletionSource resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GroupMemberTurnGate.ResumeAfter(_member, () => resumed.TrySetResult());

        await Task.Delay(50);
        Assert.False(resumed.Task.IsCompleted);
        held.Dispose();
        await resumed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        TaskCompletionSource immediate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GroupMemberTurnGate.ResumeAfter(_member, () => immediate.TrySetResult());
        await immediate.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>一个接回抛了，同一次放闸约的其它接回照样做</summary>
    [Fact]
    public async Task ResumeAfter_OneThrowing_DoesNotSkipTheOthers()
    {
        IDisposable held = GroupMemberTurnGate.TryEnter(_member)!;
        TaskCompletionSource second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GroupMemberTurnGate.ResumeAfter(_member, () => throw new InvalidOperationException("boom"));
        GroupMemberTurnGate.ResumeAfter(_member, () => second.TrySetResult());

        held.Dispose();
        await second.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>等闸时用户停下私聊：抛取消，闸不被它占着</summary>
    [Fact]
    public async Task PrivateEntry_CancelledWhileWaiting_LeavesTheGateFree()
    {
        IDisposable held = GroupMemberTurnGate.TryEnter(_member)!; //不让抢占的持闸者
        using CancellationTokenSource stop = new();
        Task<IDisposable> privateTurn = GroupMemberTurnGate.EnterPrivateAsync(_member, stop.Token);

        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => privateTurn);

        held.Dispose();
        using IDisposable? again = GroupMemberTurnGate.TryEnter(_member);
        Assert.NotNull(again);
    }
}
