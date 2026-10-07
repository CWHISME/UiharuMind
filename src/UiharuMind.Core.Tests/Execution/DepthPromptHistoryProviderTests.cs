using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

public class DepthPromptHistoryProviderTests
{
    private static List<ChatMessage> Messages(int count)
    {
        List<ChatMessage> messages = [];
        for (int i = 0; i < count; i++) messages.Add(new ChatMessage(ChatRole.User, $"消息{i}"));
        return messages;
    }

    [Fact]
    public void Splice_DepthZero_AppendsAtEnd()
    {
        List<ChatMessage> messages = Messages(3);

        DepthPromptHistoryProvider.Splice(messages, "提示", ChatRole.System, 0);

        Assert.Equal(4, messages.Count);
        Assert.Equal("提示", messages[^1].Text);
    }

    [Fact]
    public void Splice_DepthOne_InsertsBeforeLastMessage()
    {
        List<ChatMessage> messages = Messages(3);

        DepthPromptHistoryProvider.Splice(messages, "提示", ChatRole.System, 1);

        Assert.Equal(4, messages.Count);
        Assert.Equal("提示", messages[2].Text);
        Assert.Equal("消息2", messages[3].Text);
    }

    [Fact]
    public void Splice_DepthBeyondHistory_InsertsAtStart()
    {
        List<ChatMessage> messages = Messages(2);

        DepthPromptHistoryProvider.Splice(messages, "提示", ChatRole.System, 99);

        Assert.Equal(3, messages.Count);
        Assert.Equal("提示", messages[0].Text);
    }

    [Fact]
    public void Splice_EmptyPrompt_DoesNothing()
    {
        List<ChatMessage> messages = Messages(2);

        DepthPromptHistoryProvider.Splice(messages, "   ", ChatRole.System, 1);

        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void Splice_EmptyHistory_DoesNothing()
    {
        List<ChatMessage> messages = [];

        DepthPromptHistoryProvider.Splice(messages, "提示", ChatRole.System, 0);

        Assert.Empty(messages);
    }
}