using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 「这一发确实在加密思考，把服务端报的思考 token 补到那张卡上」这条信号。
///
/// 判据只有一个：<b>流里带回了密文</b>——chat 线由 HTTP 层从 <c>reasoning_details</c> 里切出
/// 加密条目，responses 线由 SDK 解出 <c>ProtectedData</c>。
///
/// <b>「正文 0 字 + usage 报了思考 token」不是证据</b>：明文思考的模型（deepseek 这类）在
/// 工具调用这一发上就是不产生思考正文，usage 却仍报计数（实测恒为 1）。按计数判会让界面
/// 凭空冒出一张「加密思考」卡，这是 2026-11-01 修掉的那个误报（见 ADR 0100 修订）。
///
/// 钉住三端：客户端在什么条件下发、落盘时怎么变成消息上的标记、标记会不会留在内容里跟着历史走。
/// </summary>
public class ThinkingNotReturnedSignalTests
{
    /// <summary>
    /// 只报计数、没有密文：不发。这是开源模型那条误报路径的回归钉子——
    /// 从前这里会发，界面于是长出一张「加密思考 · 1 思考 token」。
    /// </summary>
    [Fact]
    public async Task Client_EmitsNothing_WhenOnlyReasoningTokensReported()
    {
        LazyChatClient client = new(() => Running(new CannedClient(
            new TextContent("答"),
            Usage(790))));

        List<AIContent> seen = await Collect(client);

        Assert.Empty(seen.OfType<ThinkingNotReturnedContent>());
    }

    /// <summary>工具调用发：正文与思考都没有，usage 报 1 个思考 token（deepseek 实测形态）</summary>
    [Fact]
    public async Task Client_EmitsNothing_ForToolCallTurnWithoutReasoning()
    {
        LazyChatClient client = new(() => Running(new CannedClient(
            [
                new FunctionCallContent("call-1", "read_file", new Dictionary<string, object?> { ["path"] = "a.cs" }),
                Usage(1),
            ],
            ChatFinishReason.ToolCalls)));

        List<AIContent> seen = await Collect(client);

        Assert.Empty(seen.OfType<ThinkingNotReturnedContent>());
    }

    /// <summary>思考正文回来了就不发：明文思考的模型一张卡都不该多出来</summary>
    [Fact]
    public async Task Client_EmitsNothing_WhenReasoningTextArrived()
    {
        LazyChatClient client = new(() => Running(new CannedClient(
            new TextReasoningContent("先看一眼工具定义。"),
            new TextContent("答"),
            Usage(790))));

        List<AIContent> seen = await Collect(client);

        Assert.Empty(seen.OfType<ThinkingNotReturnedContent>());
    }

    /// <summary>只有空白思考、没有密文：同样不发（空白从来不是「想过」的证据）</summary>
    [Fact]
    public async Task Client_EmitsNothing_ForWhitespaceOnlyReasoning()
    {
        LazyChatClient client = new(() => Running(new CannedClient(
            new TextReasoningContent(" "),
            new TextContent("答"),
            Usage(1))));

        List<AIContent> seen = await Collect(client);

        Assert.Empty(seen.OfType<ThinkingNotReturnedContent>());
    }

    /// <summary>responses 线：SDK 解出的密文本身就是证据，带上 token 数发一条</summary>
    [Fact]
    public async Task Client_EmitsSignal_WhenProtectedDataArrived()
    {
        LazyChatClient client = new(() => Running(new CannedClient(
            new TextReasoningContent(string.Empty) { ProtectedData = "BLOB" },
            new TextContent("答"),
            Usage(790))));

        List<AIContent> seen = await Collect(client);

        ThinkingNotReturnedContent signal = Assert.Single(seen.OfType<ThinkingNotReturnedContent>());
        Assert.Equal(790, signal.ReasoningTokens);
    }

    /// <summary>chat 线：HTTP 层从 reasoning_details 切出加密条目时置的位，是同一份证据</summary>
    [Fact]
    public async Task Client_EmitsSignal_WhenEncryptedTicketSeen()
    {
        LazyChatClient client = new(() => Running(new CannedClient(
            [new TextContent("答"), Usage(790)],
            ChatFinishReason.Stop,
            onFirstFrame: () => LlmRequestScope.Current.EncryptedReasoningObserved?.Invoke(
                """[{"type":"reasoning.encrypted","data":"Q-PaD"}]"""u8.ToArray()))));

        List<AIContent> seen = await Collect(client);

        ThinkingNotReturnedContent signal = Assert.Single(seen.OfType<ThinkingNotReturnedContent>());
        Assert.Equal(790, signal.ReasoningTokens);
    }

    /// <summary>没有任何密文的消息一个字都不盖：那只是 usage 在报计数，曾经就是靠它凭空长出假卡</summary>
    [Fact]
    public void Persist_SkipsMarkWhenNoCiphertext()
    {
        ChatSession session = new("test", new CharacterData { CharacterId = "test" }) { IsTransient = true };
        ChatMessage reply = new(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("call-1", "read_file"),
            new ThinkingNotReturnedContent(1),
        });

        SessionChatHistoryProvider.AppendOwned(session, [reply], DateTimeOffset.Now);

        ChatMessage stored = Assert.Single(session.History);
        Assert.False(ChatMessageAnnotations.TryReadThinkingUnavailable(stored, out _));
    }

    /// <summary>
    /// 带密文的加密思考：转持久形态后标记照盖，而密文本身不会被当成「空白思考」摘掉——
    /// 它是票据，不是正文。
    /// </summary>
    [Fact]
    public void Persist_KeepsCiphertextAndMarksIt()
    {
        ChatSession session = new("test", new CharacterData { CharacterId = "test" }) { IsTransient = true };
        ChatMessage reply = new(ChatRole.Assistant, new List<AIContent>
        {
            EncryptedReasoningContent.WithTicket("""[{"type":"reasoning.encrypted","data":"x"}]""", "openrouter", "muse"),
            new ThinkingNotReturnedContent(790),
        });

        SessionChatHistoryProvider.AppendOwned(session, [reply], DateTimeOffset.Now);

        ChatMessage stored = Assert.Single(session.History);
        Assert.True(ChatMessageAnnotations.TryReadThinkingUnavailable(stored, out long markTokens));
        Assert.Equal(790, markTokens);
        Assert.True(ChatMessageAnnotations.HasEncryptedThinking(stored));
        Assert.DoesNotContain(stored.Contents, x => x is EncryptedReasoningContent);
        Assert.DoesNotContain(stored.Contents, x => x is ThinkingNotReturnedContent);
    }

    /// <summary>
    /// 只有一个空白的思考：落盘时当正文丢掉（留着回放就会多出一张「1 字」的卡）。
    /// 没有密文就不盖标记——这条路径上它只是个空格子，不是加密思考。
    /// </summary>
    [Fact]
    public void Persist_DropsWhitespaceOnlyReasoning()
    {
        ChatSession session = new("test", new CharacterData { CharacterId = "test" }) { IsTransient = true };
        ChatMessage reply = new(ChatRole.Assistant, new List<AIContent>
        {
            new TextReasoningContent(" "),
            new TextContent("答"),
            new ThinkingNotReturnedContent(1),
        });

        SessionChatHistoryProvider.AppendOwned(session, [reply], DateTimeOffset.Now);

        ChatMessage stored = Assert.Single(session.History);
        Assert.DoesNotContain(stored.Contents, x => x is TextReasoningContent);
        Assert.False(ChatMessageAnnotations.TryReadThinkingUnavailable(stored, out _));
    }

    /// <summary>
    /// 正文里带 &lt;think&gt; 的模型同样没有结构化思考内容，但它真的想过：
    /// 这种消息不许盖「思考未返回」标记，否则重开会话时凭空多插一张「加密思考」卡。
    /// </summary>
    [Fact]
    public void Persist_SkipsMessagesThatThoughtInsideTheText()
    {
        ChatSession session = new("test", new CharacterData { CharacterId = "test" }) { IsTransient = true };
        ChatMessage reply = new(ChatRole.Assistant, new List<AIContent>
        {
            new TextContent("<think>先想想。</think>答"),
            new ThinkingNotReturnedContent(790),
        });

        SessionChatHistoryProvider.AppendOwned(session, [reply], DateTimeOffset.Now);

        ChatMessage stored = Assert.Single(session.History);
        Assert.False(ChatMessageAnnotations.TryReadThinkingUnavailable(stored, out _));
        Assert.DoesNotContain(stored.Contents, x => x is ThinkingNotReturnedContent);
    }

    private static async Task<List<AIContent>> Collect(LazyChatClient client)
    {
        List<AIContent> seen = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hi")],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            seen.AddRange(update.Contents);
        }

        return seen;
    }

    private static UsageContent Usage(long reasoningTokens) =>
        new(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, ReasoningTokenCount = reasoningTokens });

    private static ModelRunningData Running(IChatClient inner)
    {
        RemoteModelInfo remote = new()
        {
            Config = new RemoteAgnesModelConfig
            {
                ModelName = "signal-test",
                ModelId = "agnes-3.0-flash",
            },
        };
        ModelRunningData data = new(remote);
        data.CompleteLoading(inner, loadToken: CancellationToken.None);
        return data;
    }

    /// <summary>照给定的内容吐一条流，收尾帧带 finish_reason（否则会被当成断线重发）</summary>
    private sealed class CannedClient : IChatClient
    {
        private readonly AIContent[] _contents;
        private readonly ChatFinishReason _finishReason;
        private readonly Action? _onFirstFrame;

        public CannedClient(params AIContent[] contents) : this(contents, ChatFinishReason.Stop)
        {
        }

        public CannedClient(AIContent[] contents, ChatFinishReason finishReason, Action? onFirstFrame = null)
        {
            _contents = contents;
            _finishReason = finishReason;
            _onFirstFrame = onFirstFrame;
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("本测试只走流式路径");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _onFirstFrame?.Invoke();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, _contents.ToList())
            {
                FinishReason = _finishReason,
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
