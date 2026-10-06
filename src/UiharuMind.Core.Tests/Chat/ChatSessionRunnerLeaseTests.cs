using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 执行者租约：Attach 与 Run 共用同一实例，轮次中途的释放只延迟、不换实例。
/// 钉的是实机那次「ICharacterRunner 尚未挂接会话」——Attach 的和 Run 的不是同一个实例。
/// </summary>
public class ChatSessionRunnerLeaseTests
{
    private static ChatSession NewSession()
    {
        return new ChatSession("test", new CharacterData { CharacterId = "test" }) { IsTransient = true };
    }

    /// <summary>借出的就是挂接好的那一个实例，不会多建</summary>
    [Fact]
    public async Task AcquireRunnerAsync_AttachesAndHandsOutTheSameInstance()
    {
        ChatSession session = NewSession();
        FakeRunner fake = new();
        session.SetRunnerForTest(fake);

        using ChatSession.RunnerLease lease =
            await session.AcquireRunnerAsync(TestContext.Current.CancellationToken);

        Assert.Same(fake, lease.Runner);
        Assert.Same(fake, session.Runner);
        Assert.Equal(1, fake.AttachCalls);
    }

    /// <summary>
    /// 轮次中途的释放（冷会话卸载、跨实例替换、删除）不能把实例换掉：
    /// 租约里的那一个照常跑，不炸「尚未挂接会话」
    /// </summary>
    [Fact]
    public async Task DisposeDuringLease_KeepsTheInstance_TurnStillRuns()
    {
        ChatSession session = NewSession();
        FakeRunner fake = new();
        session.SetRunnerForTest(fake);

        using ChatSession.RunnerLease lease =
            await session.AcquireRunnerAsync(TestContext.Current.CancellationToken);
        await session.DisposeRunnerAsync();

        Assert.False(fake.Disposed);
        Assert.Same(fake, session.Runner);

        List<AIContent> contents = [];
        await foreach (AIContent content in lease.Runner.RunAsync([], TestContext.Current.CancellationToken))
        {
            contents.Add(content);
        }

        Assert.Single(contents);
    }

    /// <summary>延迟的释放在最后一个租约归零后执行，之后再借是惰性重建的新实例</summary>
    [Fact]
    public async Task DeferredDispose_RunsAfterLastLeaseReleased()
    {
        ChatSession session = NewSession();
        FakeRunner fake = new();
        session.SetRunnerForTest(fake);

        ChatSession.RunnerLease lease =
            await session.AcquireRunnerAsync(TestContext.Current.CancellationToken);
        await session.DisposeRunnerAsync();
        Assert.False(fake.Disposed);

        lease.Dispose();
        for (int i = 0; i < 100 && !fake.Disposed; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(fake.Disposed);
        Assert.NotSame(fake, session.Runner);
    }

    /// <summary>挂接失败不占租约：后面的释放是即时生效的，不会被卡住的计数拖成延迟</summary>
    [Fact]
    public async Task FailedAttach_DoesNotHoldALease()
    {
        ChatSession session = NewSession();
        FakeRunner fake = new() { FailAttach = true };
        session.SetRunnerForTest(fake);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.AcquireRunnerAsync(TestContext.Current.CancellationToken));
        await session.DisposeRunnerAsync();

        Assert.True(fake.Disposed);
    }

    /// <summary>与 HarnessCharacterRunner 同口径的假执行者：没挂接或已释放就炸</summary>
    private sealed class FakeRunner : ICharacterRunner
    {
        public int AttachCalls { get; private set; }

        public bool Disposed { get; private set; }

        public bool FailAttach { get; set; }

        private bool _attached;

        public bool HasSession => _attached;

        public ETurnBusy Busy => ETurnBusy.None;

        public Action? BusyChanged { get; set; }

        public ChatOptions? ChatOptions => null;

        public Task AttachAsync(ChatSession session, CancellationToken cancellationToken = default)
        {
            AttachCalls++;
            if (FailAttach) throw new InvalidOperationException("attach failed");
            _attached = true;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<AIContent> RunAsync(IEnumerable<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!_attached || Disposed)
            {
                throw new InvalidOperationException(
                    $"{nameof(ICharacterRunner)} 尚未挂接会话。");
            }

            yield return new TextContent("ok");
            await Task.CompletedTask;
        }

        public Task SaveStateAsync() => Task.CompletedTask;

        public IReadOnlyList<ChatMessage> GetHistory() => [];

        public Task<EAgentMode> GetModeAsync() => Task.FromResult(EAgentMode.Execute);

        public Task SetModeAsync(EAgentMode mode) => Task.CompletedTask;

        public Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync() =>
            Task.FromResult<IReadOnlyList<TodoSnapshot>>([]);

        public Task<bool> TryInjectAsync(IEnumerable<ChatMessage> messages) => Task.FromResult(false);

        public Task<IReadOnlyCollection<ChatMessage>> CancelInjectionsAsync(IReadOnlyCollection<ChatMessage> messages) =>
            Task.FromResult<IReadOnlyCollection<ChatMessage>>([]);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
