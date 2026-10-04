using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 单聊的送达：醒着（一轮在跑）就插进那一轮，不等它收了再叫；没被消费就回退成追加加唤醒，一条结果只落一次
/// </summary>
public class SessionReportSinkTests
{
    private readonly FakeHost _host = new();
    private readonly SessionReportSink _sink;

    public SessionReportSinkTests()
    {
        _sink = new SessionReportSink(_host);
    }

    [Fact]
    public async Task WhileTurnRunning_InjectsIntoIt_AndDoesNotWakeAgain()
    {
        BackgroundTaskOutcome outcome = await BackgroundTaskTests.RunAsync("echo done", TimeSpan.FromSeconds(10));
        _host.Busy = true;
        _host.Runner.OnInjected = message =>
        {
            // 下一次模型调用取走它、随那一轮落盘，然后这一轮收了
            _host.Session.History.Add(message);
            _host.Busy = false;
            return true;
        };

        await _sink.DeliverAsync(outcome).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        ChatMessage report = Assert.Single(_host.Session.History);
        Assert.Equal(outcome.Task.Id, ChatMessageAnnotations.ReadBackgroundTaskReportId(report));
        Assert.Empty(_host.Wakes);
    }

    [Fact]
    public async Task InjectedButTurnEndedUnconsumed_WithdrawsThenAppendsAndWakes()
    {
        BackgroundTaskOutcome outcome = await BackgroundTaskTests.RunAsync("echo done", TimeSpan.FromSeconds(10));
        _host.Busy = true;
        _host.Runner.OnInjected = _ =>
        {
            _host.Busy = false; //这一轮没再调模型就收了，它还在队列里
            return false;
        };

        await _sink.DeliverAsync(outcome).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Single(_host.Runner.Withdrawn);
        Assert.Single(_host.Session.History);
        Assert.Single(_host.Wakes);
    }

    [Fact]
    public async Task WhenIdle_AppendsAndWakes_WithoutInjecting()
    {
        BackgroundTaskOutcome outcome = await BackgroundTaskTests.RunAsync("echo done", TimeSpan.FromSeconds(10));

        await _sink.DeliverAsync(outcome).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Empty(_host.Runner.Injected);
        Assert.Single(_host.Session.History);
        Assert.Single(_host.Wakes);
    }

    private sealed class FakeHost : ISessionReportHost
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

    /// <summary>注入队列的替身：插进来的交给 <see cref="OnInjected"/> 决定当场消费（true）还是留在队列里</summary>
    private sealed class FakeRunner : ICharacterRunner
    {
        private readonly List<ChatMessage> _queue = [];

        public Func<ChatMessage, bool> OnInjected { get; set; } = _ => false;

        public List<ChatMessage> Injected { get; } = [];

        public List<ChatMessage> Withdrawn { get; } = [];

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
            List<ChatMessage> withdrawn = messages.Where(_queue.Remove).ToList();
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
