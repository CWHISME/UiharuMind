using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 网关断了上游仍补 [DONE] 正常收尾：只剩思考、没有 finish_reason 的回复要认成截断
/// </summary>
public class StreamCompletionWatchTests
{
    private static StreamCompletionWatch Watch(params ChatResponseUpdate[] updates)
    {
        StreamCompletionWatch watch = new();
        foreach (ChatResponseUpdate update in updates) watch.Observe(update);
        return watch;
    }

    private static ChatResponseUpdate Update(params AIContent[] contents) => new(ChatRole.Assistant, contents);

    private static ChatResponseUpdate Finish(ChatFinishReason reason) =>
        new(ChatRole.Assistant, Array.Empty<AIContent>()) { FinishReason = reason };

    [Fact]
    public void ReasoningOnly_WithoutFinishReason_IsCutOff()
    {
        StreamCompletionWatch watch = Watch(
            Update(new TextReasoningContent("想一想")),
            Update(new UsageContent(new UsageDetails { OutputTokenCount = 5197 })));

        Assert.True(watch.IsCutOff);
        Assert.Null(watch.FinishReason);
    }

    [Fact]
    public void EmptyStream_IsCutOff() => Assert.True(Watch().IsCutOff);

    [Fact]
    public void ReasoningOnly_WithFinishReason_IsNotCutOff()
    {
        StreamCompletionWatch watch = Watch(Update(new TextReasoningContent("想一想")), Finish(ChatFinishReason.Stop));

        Assert.False(watch.IsCutOff);
        Assert.Equal("stop", watch.FinishReason);
    }

    [Fact]
    public void Text_WithoutFinishReason_IsNotCutOff() =>
        Assert.False(Watch(Update(new TextContent("答案"))).IsCutOff);

    [Fact]
    public void ToolCall_WithoutFinishReason_IsNotCutOff() =>
        Assert.False(Watch(Update(new FunctionCallContent("c1", "Shell"))).IsCutOff);

    [Fact]
    public void EmptyText_DoesNotCountAsAnswer() =>
        Assert.True(Watch(Update(new TextContent(""))).IsCutOff);
}
