/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 同一子会话的后台轮次必须串行：执行者是会话本体的惰性单例，两轮一旦重叠，
/// 后一轮 <c>Attach</c> 完、前一轮 <c>finally</c> 就把 runner 释放掉，
/// 读到的就是个没挂接的新 runner，当场炸「尚未挂接会话」
/// （实机：同一子会话同秒结束两轮，一轮正常、一轮空报告，还附带多份同时交回）。
/// 不起模型——跑的是调用方给的委托。
/// </summary>
public class BackgroundSubAgentSerializationTests
{
    [Fact]
    public async Task ConcurrentDispatches_OnTheSameSubSession_RunOneAfterAnother()
    {
        ChatSession session = new() { SessionId = "serializetestsub0001", ParentSessionId = "nosuchparent" };
        List<string> order = new();
        object locker = new();
        void Note(string step)
        {
            lock (locker) order.Add(step);
        }

        List<string> Snapshot()
        {
            lock (locker) return order.ToList();
        }

        TaskCompletionSource firstRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        BackgroundSubAgentDispatcher.Dispatch(session, async _ =>
        {
            Note("first-enter");
            await firstRelease.Task.ConfigureAwait(false);
            Note("first-exit");
            return "first";
        });

        // 第一轮确实拿住闸门之后再派第二轮，否则谁先起是调度运气
        for (int i = 0; i < 200 && !Snapshot().Contains("first-enter"); i++)
            await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.Contains("first-enter", Snapshot());

        BackgroundSubAgentDispatcher.Dispatch(session, _ =>
        {
            Note("second-enter");
            return Task.FromResult("second");
        });

        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("second-enter", Snapshot()); //还压在闸门外，没开跑

        firstRelease.SetResult();
        for (int i = 0; i < 200 && BackgroundSubAgentDispatcher.IsAwaitingReport(session.SessionId); i++)
            await Task.Delay(25, TestContext.Current.CancellationToken);

        Assert.Equal(["first-enter", "first-exit", "second-enter"], Snapshot());
    }
}
