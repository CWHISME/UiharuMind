using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Delivery;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 送信（ADR 0062）：收信人醒着就插进那一轮，被取走了就不再追加、也不再叫醒；
/// 那一轮没取走就收了，撤回来落进历史再叫醒；闲着直接落再叫醒
/// </summary>
public class SessionDeliveryTests
{
    private readonly FakeHost _host = new();
    private readonly SessionDelivery _delivery;

    public SessionDeliveryTests()
    {
        _delivery = new SessionDelivery(_host);
    }

    [Fact]
    public async Task WhileTurnRunning_InjectsIntoIt_AndDoesNotWakeAgain()
    {
        _host.Busy = true;
        _host.Runner.OnInjected = message =>
        {
            // 下一次模型调用取走它、随那一轮落盘，然后这一轮收了
            _host.Session.History.Add(message);
            _host.Busy = false;
            return true;
        };

        EDeliveryOutcome outcome = await DeliverAsync(new TestLetter(_host.Session));

        Assert.Equal(EDeliveryOutcome.Consumed, outcome);
        Assert.Single(_host.Session.History);
        Assert.Empty(_host.Wakes);
    }

    /// <summary>
    /// 取走了不等于送到：它随那次调用结束才落盘，中间进程没了就丢。进了历史才算，
    /// 但那一轮还长着呢，也不陪它跑完
    /// </summary>
    [Fact]
    public async Task ConsumedMidTurn_CountsOnlyOnceInHistory_NotWhenTheTurnEnds()
    {
        _host.Busy = true;
        ChatMessage? taken = null;
        _host.Runner.OnInjected = message =>
        {
            taken = message;
            return true;
        };

        Task<EDeliveryOutcome> delivering = DeliverAsync(new TestLetter(_host.Session));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(delivering.IsCompleted);

        _host.Session.History.Add(taken!); //那次调用结束，随之落盘
        Assert.Equal(EDeliveryOutcome.Consumed, await delivering);
        Assert.True(_host.Busy);
        Assert.Empty(_host.Wakes);
    }

    /// <summary>刚取走就被停、那次调用没发出去：它不会落盘，那就自己落、再叫醒</summary>
    [Fact]
    public async Task ConsumedButNeverPersisted_WritesItselfOnceTheTurnEnds()
    {
        _host.Busy = true;
        _host.Runner.OnInjected = _ =>
        {
            _host.Busy = false;
            return true;
        };

        EDeliveryOutcome outcome = await DeliverAsync(new TestLetter(_host.Session));

        Assert.Equal(EDeliveryOutcome.Written, outcome);
        Assert.Single(_host.Session.History);
        Assert.Single(_host.Wakes);
    }

    /// <summary>执行者已经换掉、队列跟着没了：撤不动也不能当成送到了</summary>
    [Fact]
    public async Task WithdrawImpossible_WritesInstead()
    {
        _host.Busy = true;
        _host.Runner.CannotWithdraw = true;
        _host.Runner.OnInjected = _ =>
        {
            _host.Busy = false;
            return false;
        };

        EDeliveryOutcome outcome = await DeliverAsync(new TestLetter(_host.Session));

        Assert.Equal(EDeliveryOutcome.Written, outcome);
        Assert.Single(_host.Session.History);
    }

    [Fact]
    public async Task InjectedButTurnEndedUnconsumed_WithdrawsThenWritesAndWakes()
    {
        _host.Busy = true;
        _host.Runner.OnInjected = _ =>
        {
            _host.Busy = false; //这一轮没再调模型就收了，它还在队列里
            return false;
        };

        EDeliveryOutcome outcome = await DeliverAsync(new TestLetter(_host.Session));

        Assert.Equal(EDeliveryOutcome.Written, outcome);
        Assert.Single(_host.Runner.Withdrawn);
        Assert.Single(_host.Session.History);
        Assert.Single(_host.Wakes);
    }

    [Fact]
    public async Task WhenIdle_WritesAndWakes_WithoutInjecting()
    {
        EDeliveryOutcome outcome = await DeliverAsync(new TestLetter(_host.Session));

        Assert.Equal(EDeliveryOutcome.Written, outcome);
        Assert.Empty(_host.Runner.Injected);
        Assert.Single(_host.Session.History);
        Assert.Single(_host.Wakes);
    }

    /// <summary>没有可插的（如回信正文还没有）：不插，等收信人闲下来照常落</summary>
    [Fact]
    public async Task NothingToInsert_WaitsForIdleThenWrites()
    {
        _host.Busy = true;
        TestLetter letter = new(_host.Session) { Insertable = false };
        Task<EDeliveryOutcome> delivering = DeliverAsync(letter);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _host.Busy = false;

        Assert.Equal(EDeliveryOutcome.Written, await delivering);
        Assert.Empty(_host.Runner.Injected);
    }

    /// <summary>退出收尾与正常送达可能都走到：同一个后台任务只留一条</summary>
    [Fact]
    public async Task BackgroundTaskResult_IsWrittenOnceByTaskId()
    {
        BackgroundTaskOutcome outcome = await BackgroundTaskTests.RunAsync("echo done", TimeSpan.FromSeconds(10));
        SessionReportSink sink = new(_delivery);
        _host.Session.SessionId = outcome.Task.OwnerSessionId;

        await sink.DeliverAsync(outcome).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        sink.DeliverOnShutdown(outcome);

        ChatMessage report = Assert.Single(_host.Session.History);
        Assert.Equal(outcome.Task.Id, ChatMessageAnnotations.ReadBackgroundTaskReportId(report));
    }

    private Task<EDeliveryOutcome> DeliverAsync(SessionLetter letter) =>
        _delivery.DeliverAsync(letter).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    private sealed class TestLetter(ChatSession session) : SessionLetter
    {
        private readonly ChatMessage _message = new(ChatRole.User, "信");

        public bool Insertable { get; init; } = true;

        public override string SessionId => session.SessionId;

        public override string Cause => "test";

        public override ChatMessage? Compose(ChatSession target) => Insertable ? _message : null;

        public override ELetterWrite Write(ChatSession target)
        {
            target.History.Add(_message);
            return ELetterWrite.Written;
        }
    }

    private sealed class FakeHost : ISessionDeliveryHost
    {
        public ChatSession Session { get; } = new() { IsTransient = true };

        public FakeRunner Runner { get; } = new();

        public volatile bool Busy;

        public List<string> Wakes { get; } = [];

        public TimeSpan BusyRetryInterval => TimeSpan.FromMilliseconds(10);

        public ChatSession? Load(string sessionId) => Session;

        public bool IsBusy(string sessionId) => Busy;

        public ICharacterRunner RunnerOf(ChatSession session) => Runner;

        public Task WakeAsync(string sessionId, string cause)
        {
            Wakes.Add(cause);
            return Task.CompletedTask;
        }
    }

    /// <summary>注入队列的替身：插进来的交给 <see cref="OnInjected"/> 决定当场取走（true）还是留在队列里</summary>
    private sealed class FakeRunner : ICharacterRunner
    {
        private readonly List<ChatMessage> _queue = [];

        public Func<ChatMessage, bool> OnInjected { get; set; } = _ => false;

        public bool CannotWithdraw { get; set; }

        public List<ChatMessage> Injected { get; } = [];

        public List<ChatMessage> Withdrawn { get; } = [];

        public IReadOnlyList<ChatMessage> PendingInjections => _queue.ToList();

        public bool HasSession => true;

        public ETurnBusy Busy => ETurnBusy.None;

        public Action? BusyChanged { get; set; }

        public ChatOptions? ChatOptions => null;

        public Task<bool> TryInjectAsync(IEnumerable<ChatMessage> messages)
        {
            foreach (ChatMessage message in messages)
            {
                Injected.Add(message);
                if (!OnInjected(message)) _queue.Add(message);
            }

            return Task.FromResult(true);
        }

        public Task<IReadOnlyCollection<ChatMessage>> CancelInjectionsAsync(IReadOnlyCollection<ChatMessage> messages)
        {
            List<ChatMessage> withdrawn = CannotWithdraw ? [] : messages.Where(_queue.Remove).ToList();
            Withdrawn.AddRange(withdrawn);
            return Task.FromResult<IReadOnlyCollection<ChatMessage>>(withdrawn);
        }

        public async IAsyncEnumerable<AIContent> RunAsync(IEnumerable<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task AttachAsync(ChatSession session, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveStateAsync() => Task.CompletedTask;

        public IReadOnlyList<ChatMessage> GetHistory() => [];

        public Task<EAgentMode> GetModeAsync() => Task.FromResult(EAgentMode.Execute);

        public Task SetModeAsync(EAgentMode mode) => Task.CompletedTask;

        public Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync() =>
            Task.FromResult<IReadOnlyList<TodoSnapshot>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
