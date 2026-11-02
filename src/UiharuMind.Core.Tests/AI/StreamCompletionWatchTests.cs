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

    [Fact]
    public void TextThenThousandsOfNewlines_IsWhitespaceFlooded()
    {
        // 实测 ling-3.1-flash 的原样：一句话 + 2948 个换行，finish_reason=stop，没有工具调用
        StreamCompletionWatch watch = Watch(
            Update(new TextContent("文件被占用，换 Shell 读。")),
            Update(new TextContent(new string('\n', 2948))),
            Finish(ChatFinishReason.Stop));

        Assert.True(watch.IsWhitespaceFlooded);
        Assert.False(watch.IsCutOff); // 有 finish_reason，不算截断
        Assert.Equal(2948, watch.TrailingWhitespace);
    }

    [Fact]
    public void FloodSplitAcrossDeltas_IsAccumulated()
    {
        StreamCompletionWatch watch = new();
        watch.Observe(Update(new TextContent("说完了")));
        for (int i = 0; i < 500; i++) watch.Observe(Update(new TextContent("\n")));

        Assert.True(watch.IsWhitespaceFlooded);
    }

    [Fact]
    public void FloodThenToolCall_IsNotFlooded()
    {
        // 后面还接了工具调用，回合不会停，不必重发
        StreamCompletionWatch watch = Watch(
            Update(new TextContent("先看看。")),
            Update(new TextContent(new string('\n', 2000))),
            Update(new FunctionCallContent("c1", "Read")));

        Assert.False(watch.IsWhitespaceFlooded);
    }

    [Fact]
    public void NormalMarkdownTrailingBlankLines_IsNotFlooded()
    {
        StreamCompletionWatch watch = Watch(
            Update(new TextContent("# 标题\n\n正文一段。\n\n正文二段。\n\n")),
            Finish(ChatFinishReason.Stop));

        Assert.False(watch.IsWhitespaceFlooded);
    }

    [Fact]
    public void WhitespaceBeforeContent_DoesNotCount()
    {
        // 空白在中间而非尾部，不算刷屏
        StreamCompletionWatch watch = Watch(
            Update(new TextContent(new string('\n', 2000))),
            Update(new TextContent("真正的答案")),
            Finish(ChatFinishReason.Stop));

        Assert.False(watch.IsWhitespaceFlooded);
        Assert.Equal(0, watch.TrailingWhitespace);
    }
}
