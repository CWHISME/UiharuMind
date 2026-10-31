using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 「报了思考 token，却一个字正文都没回来」这条信号。
///
/// 判据的两个输入（这一发的思考正文字数、这一发的 <c>reasoning_tokens</c>）都在同一发流里，
/// 所以判定放在 <see cref="LazyChatClient"/>，不放在上层拿历史去猜——那一套要跟
/// 「边界、落盘、用量」三者的先后赛跑，实测排不齐，猜错就是把上一发的 token 盖到下一发的消息上。
///
/// 钉住三端：客户端在什么条件下发、落盘时怎么变成消息上的标记、标记会不会留在内容里跟着历史走。
/// </summary>
public class ThinkingNotReturnedSignalTests
{
    /// <summary>报了思考 token 却一个字思考正文都没有：发一条信号，带上那个数</summary>
    [Fact]
    public async Task Client_EmitsSignal_WhenReasoningTokensHaveNoText()
    {
        LazyChatClient client = new(() => Running(new CannedClient(
            new TextContent("答"),
            Usage(790))));

        List<AIContent> seen = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hi")],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            seen.AddRange(update.Contents);
        }

        ThinkingNotReturnedContent signal = Assert.Single(seen.OfType<ThinkingNotReturnedContent>());
        Assert.Equal(790, signal.ReasoningTokens);
    }

    /// <summary>思考正文回来了就不发：明文思考的模型（deepseek 这类）一张卡都不该多出来</summary>
    [Fact]
    public async Task Client_EmitsNothing_WhenReasoningTextArrived()
    {
        LazyChatClient client = new(() => Running(new CannedClient(
            new TextReasoningContent("先看一眼工具定义。"),
            new TextContent("答"),
            Usage(790))));

        List<AIContent> seen = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hi")],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            seen.AddRange(update.Contents);
        }

        Assert.Empty(seen.OfType<ThinkingNotReturnedContent>());
    }

    /// <summary>
    /// 只回一个空白（实测 deepseek 会发一个孤立空格）同样算「一个字没回」：
    /// 空白不是正文，不能拿它挡掉信号，否则界面会冒出一张「1 字」的假思考卡。
    /// </summary>
    [Fact]
    public async Task Client_TreatsWhitespaceOnlyReasoningAsNoText()
    {
        LazyChatClient client = new(() => Running(new CannedClient(
            new TextReasoningContent(" "),
            new TextContent("答"),
            Usage(1))));

        List<AIContent> seen = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hi")],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            seen.AddRange(update.Contents);
        }

        Assert.Single(seen.OfType<ThinkingNotReturnedContent>());
    }

    /// <summary>落盘时信号转成消息上的标记并摘掉自己：标记跟着消息走，重开会话才补得回那张卡</summary>
    [Fact]
    public void Persist_TurnsSignalIntoAnnotation_AndDropsTheContent()
    {
        ChatSession session = new("test", new CharacterData { CharacterId = "test" }) { IsTransient = true };
        ChatMessage reply = new(ChatRole.Assistant, new List<AIContent>
        {
            new TextContent("答"),
            new ThinkingNotReturnedContent(790),
        });

        SessionChatHistoryProvider.AppendOwned(session, [reply], DateTimeOffset.Now);

        ChatMessage stored = Assert.Single(session.History);
        Assert.True(ChatMessageAnnotations.TryReadThinkingUnavailable(stored, out long tokens));
        Assert.Equal(790, tokens);
        Assert.DoesNotContain(stored.Contents, x => x is ThinkingNotReturnedContent);
    }

    /// <summary>
    /// 只有一个空白的思考：落盘时当正文丢掉，标记照样盖——
    /// 留着那个空格，回放就会多出一张「1 字」的卡。
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
        Assert.True(ChatMessageAnnotations.TryReadThinkingUnavailable(stored, out long tokens));
        Assert.Equal(1, tokens);
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
    private sealed class CannedClient(params AIContent[] contents) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("本测试只走流式路径");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, contents.ToList())
            {
                FinishReason = ChatFinishReason.Stop,
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
