using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 「思考没返回」那张卡的归属（<c>ThinkingNotReturned</c> 兜底路径）。
///
/// 判据只有两个输入：这一发报的思考 token，和这一发产出的助手消息。两者的到达顺序不固定——
/// 实测 DeepSeek 系是<b>边界先过、带用量的那一帧后到</b>（<c>StreamUsage.KeepLast</c> 把用量挪到流尾）。
/// 早先把 token 留在字段里等下一个边界消费，于是上一发的数被盖到下一发头上：下一发只是调了个工具、
/// 思考与正文都没有，界面就凭空冒出一张「加密思考」卡，紧贴上一发真实的思考卡——
/// 2026-10-31 一轮里连出六张，把明文思考的 deepseek 记成了闭源模型。
/// </summary>
public class ThinkingNotReturnedAttributionTests
{
    private static ChatSession NewSession() =>
        new("test", new CharacterData { CharacterId = "test" }) { IsTransient = true };

    private static ChatMessage Prompt(string text = "开工") => new(ChatRole.User, text);

    /// <summary>
    /// 上一发报了思考 token，下一发只是调了个工具：下一发<b>不能</b>被记成「思考没返回」。
    /// </summary>
    [Fact]
    public async Task ReasoningTokens_AreNotCarriedToTheNextCall()
    {
        ChatSession session = NewSession();
        ChatMessage second = new(ChatRole.Assistant,
            new List<AIContent> { new FunctionCallContent("c1", "Read", null) });
        RecordingRunner runner = new(session, 
            new CallScript(new ChatMessage(ChatRole.Assistant,
                new List<AIContent> { new TextReasoningContent("先看一眼工具定义。") }), 18),
            new CallScript(second, 0));
        RecordingSink sink = new();
        TurnDriver driver = new(sink, new TurnUsageLedger());

        await driver.RunAsync(session, runner, Prompt(), externalCancellation: TestContext.Current.CancellationToken);
        await driver.RunAsync(session, runner, Prompt("继续"), externalCancellation: TestContext.Current.CancellationToken);

        Assert.False(ChatMessageAnnotations.TryReadThinkingUnavailable(second, out _));
        Assert.Equal(0, sink.ThinkingNotReturnedCalls);
    }

    /// <summary>
    /// 反过来：这一发确实报了思考 token 却一个字都没有（真正的加密思考），边界先到也照样要补卡。
    /// </summary>
    [Fact]
    public async Task LateUsage_StillMarksTheCallItBelongsTo()
    {
        ChatSession session = NewSession();
        ChatMessage reply = new(ChatRole.Assistant, new List<AIContent> { new TextContent("好。") });
        RecordingRunner runner = new(session, new CallScript(reply, 790));
        RecordingSink sink = new();
        TurnDriver driver = new(sink, new TurnUsageLedger());

        await driver.RunAsync(session, runner, Prompt(), externalCancellation: TestContext.Current.CancellationToken);

        Assert.True(ChatMessageAnnotations.TryReadThinkingUnavailable(reply, out long tokens));
        Assert.Equal(790, tokens);
        Assert.Equal(1, sink.ThinkingNotReturnedCalls);
    }

    /// <summary>一次服务调用的脚本：落一条助手消息进历史，再吐边界与用量</summary>
    private sealed record CallScript(ChatMessage Reply, long ReasoningTokens);

    private sealed class RecordingRunner(ChatSession session, params CallScript[] calls) : ICharacterRunner
    {
        private readonly Queue<CallScript> _calls = new(calls);

        public ETurnBusy Busy => ETurnBusy.None;

        public bool HasSession => true;

        public Action? BusyChanged { get; set; }

        public ChatOptions? ChatOptions => null;

        public TurnInputEstimate? InputEstimate => null;

        public async IAsyncEnumerable<AIContent> RunAsync(IEnumerable<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_calls.Count == 0) yield break;

            CallScript call = _calls.Dequeue();
            // 线上是「先落盘、再放边界」：历史里此刻已经有了这一发的助手消息
            session.History.Add(call.Reply);
            yield return MessageBoundaryContent.Instance;
            await Task.Yield();

            yield return new UsageContent(new UsageDetails
            {
                InputTokenCount = 100,
                OutputTokenCount = 10,
                ReasoningTokenCount = call.ReasoningTokens,
            });
        }

        public Task AttachAsync(ChatSession target, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveStateAsync() => Task.CompletedTask;

        public IReadOnlyList<ChatMessage> GetHistory() => [];

        public Task<EAgentMode> GetModeAsync() => Task.FromResult(EAgentMode.Execute);

        public Task SetModeAsync(EAgentMode mode) => Task.CompletedTask;

        public Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync() =>
            Task.FromResult<IReadOnlyList<TodoSnapshot>>([]);

        public Task<bool> TryInjectAsync(IEnumerable<ChatMessage> messages) => Task.FromResult(false);

        public Task<IReadOnlyCollection<ChatMessage>> CancelInjectionsAsync(IReadOnlyCollection<ChatMessage> messages) =>
            Task.FromResult<IReadOnlyCollection<ChatMessage>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSink : ITurnSink
    {
        public int ThinkingNotReturnedCalls { get; private set; }

        public void Apply(AIContent content)
        {
        }

        public void CloseSegment()
        {
        }

        public void StopRunningToolCalls(string note)
        {
        }

        public string? TakeStreamingText() => null;

        public void ThinkingNotReturned(long reasoningTokens) => ThinkingNotReturnedCalls++;
    }
}
