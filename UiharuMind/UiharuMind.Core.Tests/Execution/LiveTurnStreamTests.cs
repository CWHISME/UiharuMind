using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 实时内容的分岔口：一条流，多个看客。
/// 它替掉的是「观察者只能等落盘」——那条路上工具结果要晚整整一次模型调用才出现。
/// </summary>
public class LiveTurnStreamTests
{
    private sealed class RecordingSink : ITurnSink
    {
        public List<string> Texts { get; } = new();
        public int Closed { get; private set; }
        public string? StoppedNote { get; private set; }
        public string? StreamingText { get; set; }
        public int TakenStreamingText { get; private set; }

        public void Apply(AIContent content)
        {
            if (content is TextContent text) Texts.Add(text.Text);
        }

        public void CloseSegment() => Closed++;

        public void StopRunningToolCalls(string note) => StoppedNote = note;

        public string? TakeStreamingText()
        {
            TakenStreamingText++;
            return StreamingText;
        }
    }

    private sealed class ThrowingSink : ITurnSink
    {
        public void Apply(AIContent content) => throw new InvalidOperationException("boom");

        public void CloseSegment() => throw new InvalidOperationException("boom");

        public void StopRunningToolCalls(string note) => throw new InvalidOperationException("boom");

        public string? TakeStreamingText() => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Content_ReachesBothTheDriverAndTheObserver()
    {
        LiveTurnStream stream = new();
        RecordingSink primary = new();
        RecordingSink observer = new();
        using IDisposable subscription = stream.Observe(observer);

        using LiveTurnStream.Scope scope = stream.BeginTurn(primary);
        scope.Sink.Apply(new TextContent("你好"));

        Assert.Equal(["你好"], primary.Texts);
        Assert.Equal(["你好"], observer.Texts);
    }

    /// <summary>
    /// 同一个界面既是本轮的驱动者、又挂着观察（它的订阅比一轮长命）。
    /// 不按身份去重的话，自己发的内容自己收两遍——每条消息显示两次。
    /// </summary>
    [Fact]
    public void TheDriverIsNotFedTwiceWhenItAlsoObserves()
    {
        LiveTurnStream stream = new();
        RecordingSink transcript = new();
        // 观察时外面包了一层线程 marshal,所以 sink 与身份不是同一个对象
        RecordingSink wrapper = new();
        using IDisposable subscription = stream.Observe(wrapper, identity: transcript);

        using LiveTurnStream.Scope scope = stream.BeginTurn(transcript);
        scope.Sink.Apply(new TextContent("你好"));

        Assert.Equal(["你好"], transcript.Texts);
        Assert.Empty(wrapper.Texts);
    }

    /// <summary>
    /// 「跑着的时候点开看看」正是子会话窗口存在的理由：中途挂上来要能看到本轮已经发生的。
    /// </summary>
    [Fact]
    public void AnObserverJoiningMidTurnGetsWhatHasNotBeenPersistedYet()
    {
        LiveTurnStream stream = new();
        using LiveTurnStream.Scope scope = stream.BeginTurn(new RecordingSink());
        scope.Sink.Apply(new TextContent("第一段"));
        scope.Sink.Apply(new TextContent("第二段"));

        RecordingSink latecomer = new();
        using IDisposable subscription = stream.Observe(latecomer);

        Assert.Equal(["第一段", "第二段"], latecomer.Texts);
    }

    /// <summary>
    /// 落了盘的那一段观察者自己从历史读得到。还补发的话它会渲染两遍，
    /// 而这份缓冲也会随一轮的长度一直涨。
    /// </summary>
    [Fact]
    public void PersistedContentIsNotReplayedToALatecomer()
    {
        LiveTurnStream stream = new();
        using LiveTurnStream.Scope scope = stream.BeginTurn(new RecordingSink());
        scope.Sink.Apply(new TextContent("已落盘"));
        stream.NoteHistoryPersisted();
        scope.Sink.Apply(new TextContent("还没落盘"));

        RecordingSink latecomer = new();
        using IDisposable subscription = stream.Observe(latecomer);

        Assert.Equal(["还没落盘"], latecomer.Texts);
    }

    [Fact]
    public void TurnEndSignalsReachObservers()
    {
        LiveTurnStream stream = new();
        RecordingSink observer = new();
        using IDisposable subscription = stream.Observe(observer);

        using (LiveTurnStream.Scope scope = stream.BeginTurn(new RecordingSink()))
        {
            scope.Sink.CloseSegment();
            scope.Sink.StopRunningToolCalls("stopped");
        }

        Assert.Equal(1, observer.Closed);
        Assert.Equal("stopped", observer.StoppedNote);
    }

    /// <summary>
    /// 取消时要落库的正文只认驱动者那一份——观察者的条目不进任何人的历史。
    /// 但观察者仍要走一遍，因为 TakeStreamingText 同时是「收尾当前流段」。
    /// </summary>
    [Fact]
    public void StreamingTextForPersistenceComesFromTheDriverOnly()
    {
        LiveTurnStream stream = new();
        RecordingSink primary = new() { StreamingText = "半截回复" };
        RecordingSink observer = new() { StreamingText = "观察者的那份" };
        using IDisposable subscription = stream.Observe(observer);

        using LiveTurnStream.Scope scope = stream.BeginTurn(primary);

        Assert.Equal("半截回复", scope.Sink.TakeStreamingText());
        Assert.Equal(1, observer.TakenStreamingText);
    }

    /// <summary>看客炸了不能带倒这一轮：子代理还得把报告交回去</summary>
    [Fact]
    public void AFailingObserverDoesNotBreakTheTurn()
    {
        LiveTurnStream stream = new();
        RecordingSink primary = new();
        using IDisposable subscription = stream.Observe(new ThrowingSink());

        using LiveTurnStream.Scope scope = stream.BeginTurn(primary);
        scope.Sink.Apply(new TextContent("你好"));
        scope.Sink.CloseSegment();
        scope.Sink.StopRunningToolCalls("stopped");

        Assert.Equal(["你好"], primary.Texts);
    }

    [Fact]
    public void AnUnsubscribedObserverStopsReceiving()
    {
        LiveTurnStream stream = new();
        RecordingSink observer = new();
        IDisposable subscription = stream.Observe(observer);
        subscription.Dispose();

        using LiveTurnStream.Scope scope = stream.BeginTurn(new RecordingSink());
        scope.Sink.Apply(new TextContent("你好"));

        Assert.Empty(observer.Texts);
    }

    /// <summary>轮末拆岔口，本轮缓冲跟着消失：下一轮的看客不该收到上一轮的尾巴</summary>
    [Fact]
    public void TheTurnBufferDiesWithTheTurn()
    {
        LiveTurnStream stream = new();
        using (LiveTurnStream.Scope scope = stream.BeginTurn(new RecordingSink()))
        {
            scope.Sink.Apply(new TextContent("上一轮"));
        }

        Assert.False(stream.IsTurnRunning);

        RecordingSink latecomer = new();
        using IDisposable subscription = stream.Observe(latecomer);

        Assert.Empty(latecomer.Texts);
    }
}
