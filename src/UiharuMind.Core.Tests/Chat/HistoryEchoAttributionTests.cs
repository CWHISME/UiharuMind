using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 框架回灌历史时往带附加属性的消息上就地盖「来源 = 历史」的 _attribution。
/// 它们是我们自己的历史，不是注入——界面按键在不在判会把点名调用、群投递、私聊在重放时藏掉
/// </summary>
public class HistoryEchoAttributionTests
{
    [Fact]
    public void HistorySource_IsAnEcho_InMemoryAndAfterRoundTrip()
    {
        ChatMessage stamped = Stamp(AgentRequestMessageSourceType.ChatHistory);

        Assert.True(ChatMessageAnnotations.IsHistoryEcho(stamped));
        Assert.True(ChatMessageAnnotations.IsHistoryEcho(RoundTrip(stamped)));
    }

    [Fact]
    public void ContextProviderSource_IsNotAnEcho()
    {
        ChatMessage injected = Stamp(AgentRequestMessageSourceType.AIContextProvider);

        Assert.False(ChatMessageAnnotations.IsHistoryEcho(injected));
        Assert.False(ChatMessageAnnotations.IsHistoryEcho(RoundTrip(injected)));
        Assert.False(ChatMessageAnnotations.IsHistoryEcho(new ChatMessage(ChatRole.User, "没盖过")));
    }

    private static ChatMessage Stamp(AgentRequestMessageSourceType type)
    {
        ChatMessage message = new(ChatRole.User, "hi");
        ChatMessageAnnotations.MarkGroupDelivery(message);
        return message.WithAgentRequestMessageSource(type, "source");
    }

    private static ChatMessage RoundTrip(ChatMessage message) =>
        JsonSerializer.Deserialize<ChatMessage>(JsonSerializer.Serialize(message, SessionJsonOptions.Default),
            SessionJsonOptions.Default)!;
}
