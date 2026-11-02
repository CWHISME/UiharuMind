/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 会话历史是<b>跨线程</b>共用的：写方在执行线程（每次服务调用落盘、子代理交回报告、群投递），
/// 读方在 UI 线程（对账、渲染、搜索、用量统计）。
///
/// 这一组测试守的是那个曾经的崩溃：写方 <c>Add</c> 的同时，读方
/// <c>new HashSet&lt;ChatMessage&gt;(history, …)</c> 整表枚举，抛
/// <c>Collection was modified; enumeration operation may not execute</c>
/// （<c>ConversationItemActions.WireStreamed</c>）。修法是把历史换成
/// <see cref="SessionHistory"/>：单条读写各自持锁，跨多条操作的读取
/// <see cref="SessionHistory.Snapshot"/>。
///
/// 若哪天有人把它换回裸 <c>List</c>，这几个用例会当场炸。
/// </summary>
public class SessionHistoryConcurrencyTests
{
    private const int Writes = 20_000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task SnapshotAndEnumerate_WhileWriterAppends_NeverThrows()
    {
        SessionHistory history = new();
        using CancellationTokenSource done = new();
        using ManualResetEventSlim readerReady = new();
        Exception? failure = null;
        long reads = 0;

        Task writer = Task.Run(() =>
        {
            try
            {
                // 等读方进循环再开写：不然读方可能整个被调度到写方跑完之后，用例变成空转
                readerReady.Wait(Timeout);
                for (int i = 0; i < Writes; i++) history.Add(new ChatMessage(ChatRole.User, $"m{i}"));
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                done.Cancel();
            }
        }, TestContext.Current.CancellationToken);

        try
        {
            ReadOnce();
            readerReady.Set();
            while (!done.IsCancellationRequested) ReadOnce();
        }
        catch (Exception e)
        {
            failure = e;
        }

        await writer.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Null(failure);

        void ReadOnce()
        {
            // 对账那条路的原样复刻：先取快照，再整表构造 HashSet（就是崩在这里的）
            ChatMessage[] snapshot = history.Snapshot();
            _ = new HashSet<ChatMessage>(snapshot, ReferenceEqualityComparer.Instance);
            // 补尾巴按下标切片
            if (snapshot.Length > 0) _ = snapshot[^1];
            // 直接 foreach 活历史：枚举器吐的也是快照，不该抛
            foreach (ChatMessage message in history) _ = message.Text;
            reads++;
        }
        Assert.True(reads > 0, "the reader never got a turn");
        Assert.Equal(Writes, history.Count);
    }

    /// <summary>
    /// 按下标读的路径（补尾巴、取删除下标、群投递游标）靠的是「追加不改已有下标」。
    /// 所以 <see cref="SessionHistory.AddReturningIndex"/> 给出的下标必须一直指着自己那条——
    /// 这也是落盘起点不能再「先读 Count 再 Add」的理由：两句话之间别人插一条，起点就偏了。
    /// </summary>
    [Fact]
    public void AddReturningIndex_KeepsPointingAtItsOwnMessage_UnderConcurrentAppends()
    {
        SessionHistory history = new();
        ConcurrentBag<(ChatMessage Message, int Index)> appended = new();

        Parallel.For(0, 2_000, i =>
        {
            ChatMessage message = new(ChatRole.User, $"m{i}");
            appended.Add((message, history.AddReturningIndex(message)));
        });

        ChatMessage[] final = history.Snapshot();
        Assert.Equal(2_000, final.Length);
        Assert.Equal(2_000, appended.Select(x => x.Index).Distinct().Count());
        foreach ((ChatMessage message, int index) in appended)
        {
            Assert.Same(message, final[index]);
        }
    }

    /// <summary>快照必须是真副本：取完之后别人再追加，手里那份一个字都不该变</summary>
    [Fact]
    public void Snapshot_IsDetachedFromLaterAppends()
    {
        SessionHistory history = new([new ChatMessage(ChatRole.User, "第一句")]);

        ChatMessage[] snapshot = history.Snapshot();
        history.Add(new ChatMessage(ChatRole.Assistant, "第二句"));

        Assert.Single(snapshot);
        Assert.Equal(2, history.Count);
    }

    /// <summary>
    /// 删与读并发：按下标倒着删（压缩、删消息都是这个形状）不该把读方带崩。
    ///
    /// 两边各跑固定轮数，不靠「谁先完成」来终止——那样删除方几微秒就删完了，
    /// 读方一轮都没跑，用例就成了空转（断言 reads &gt; 1 这种写法实测会偶发失败）。
    /// </summary>
    [Fact]
    public async Task Reader_WhileAnotherThreadRemovesAt_NeverThrows()
    {
        const int Rounds = 1_000;
        SessionHistory history = new(Enumerable.Range(0, Rounds).Select(i => new ChatMessage(ChatRole.User, $"m{i}")));
        Exception? failure = null;
        long reads = 0;

        Task remover = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < Rounds && history.Count > 0; i++) history.RemoveAt(history.Count - 1);
            }
            catch (Exception e)
            {
                failure = e;
            }
        }, TestContext.Current.CancellationToken);

        try
        {
            for (int i = 0; i < Rounds; i++) ReadOnce();
        }
        catch (Exception e)
        {
            failure = e;
        }

        await remover.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Null(failure);
        Assert.Empty(history);
        Assert.Equal(Rounds, reads);

        void ReadOnce()
        {
            ChatMessage[] snapshot = history.Snapshot();
            _ = new HashSet<ChatMessage>(snapshot, ReferenceEqualityComparer.Instance);
            foreach (ChatMessage message in history) _ = message.Text;
            reads++;
        }
    }
}
