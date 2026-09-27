using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 普通对话形态供给历史时去掉工具内容：只剩工具内容的消息整条不发，带正文的只剥工具那几段，
/// 且不得改到存档里的原消息
/// </summary>
public class PromptOnlyHistoryTests
{
    private static readonly ChatMessage Ask = new(ChatRole.User, "README 第一行写了什么");
    private static readonly ChatMessage Answer = new(ChatRole.Assistant, "是标题 UiharuMind。");

    private static ChatMessage CallOnly() =>
        new(ChatRole.Assistant, [new FunctionCallContent("call_1", "Read", new Dictionary<string, object?> { ["path"] = "README.md" })]);

    private static ChatMessage Result() => new(ChatRole.Tool, [new FunctionResultContent("call_1", "# UiharuMind")]);

    [Fact]
    public void NoToolContent_ReturnsSameList()
    {
        List<ChatMessage> history = [Ask, Answer];

        Assert.Same(history, PromptOnlyHistory.StripToolContents(history));
    }

    [Fact]
    public void ToolOnlyMessages_AreDropped()
    {
        IReadOnlyList<ChatMessage> result = PromptOnlyHistory.StripToolContents([Ask, CallOnly(), Result(), Answer]);

        Assert.Equal([Ask, Answer], result);
    }

    [Fact]
    public void MixedMessage_KeepsTextWithoutTouchingOriginal()
    {
        ChatMessage mixed = new(ChatRole.Assistant,
            [new TextContent("我去看一眼。"), new FunctionCallContent("call_1", "Read")]);

        ChatMessage kept = Assert.Single(PromptOnlyHistory.StripToolContents([mixed, Result()]));

        Assert.Equal("我去看一眼。", kept.Text);
        Assert.DoesNotContain(kept.Contents, x => x is FunctionCallContent);
        Assert.Equal(2, mixed.Contents.Count);
    }
}
