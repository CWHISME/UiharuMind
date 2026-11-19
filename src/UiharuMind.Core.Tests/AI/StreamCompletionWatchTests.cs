using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 网关断了上游仍补 [DONE] 正常收尾：
/// <b>思考也是产出</b>——只剩思考、没有 finish_reason 的回复不该整段作废重发
/// （第一发的思考全部丢掉，代价远大于保留）。真正的断流在传输层抛异常走另一条路。
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

    /// <summary>
    /// 只剩思考、没有 finish_reason：思考是实打实的产出，不能当作「无产出断流」
    /// 整段重发作废——agnès 一轮 1.8 万字思考就是被这个判据白丢的
    /// </summary>
    [Fact]
    public void ReasoningOnly_WithoutFinishReason_IsNotCutOff()
    {
        StreamCompletionWatch watch = Watch(
            Update(new TextReasoningContent("想一想")),
            Update(new UsageContent(new UsageDetails { OutputTokenCount = 5197 })));

        Assert.False(watch.IsCutOff);
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

    /// <summary>空思考与空正文同口径：没内容就不算产出，仍是截断</summary>
    [Fact]
    public void EmptyReasoning_DoesNotCountAsAnswer() =>
        Assert.True(Watch(Update(new TextReasoningContent(""))).IsCutOff);

    /// <summary>
    /// 纯空白思考不算产出：实测 deepseek 会发孤立空格，照算会把「只有空格思考」当成
    /// 成功收口（与 LazyChatClient 统计口径一致；落盘侧 normalizer 保留空白是保存口径，
    /// 两者是有意的不对称）
    /// </summary>
    [Fact]
    public void WhitespaceOnlyReasoning_DoesNotCountAsAnswer() =>
        Assert.True(Watch(Update(new TextReasoningContent("   \n  "))).IsCutOff);

    /// <summary>
    /// 只有密文（Text 空、ProtectedData 有值）+ 无 finish_reason：仍是截断，重发。
    /// 密文整块一帧，半截不入库；而“完整票 + 没回答”的轮次没有答案，
    /// 重发拿完整轮次比保留一张看不见的票更值（见 StreamCompletionWatch 类注释）
    /// </summary>
    [Fact]
    public void EncryptedOnlyReasoning_WithoutFinishReason_IsCutOff()
    {
        StreamCompletionWatch watch = Watch(
            Update(new TextReasoningContent(string.Empty) { ProtectedData = "BLOB" }));

        Assert.True(watch.IsCutOff);
        Assert.Null(watch.FinishReason);
    }

    /// <summary>
    /// 本次修复的核心：思考段<b>真断流</b>（传输层报告 EOF 未见 [DONE]）时，
    /// 有思考也不再算「成功收口」——连半截都没有收完，必须整段重发
    /// </summary>
    [Fact]
    public void Thinking_WithTransportNoDone_IsCutOff()
    {
        StreamCompletionWatch watch = Watch(Update(new TextReasoningContent("想一想")));
        watch.MarkTransportEnd(sawDone: false);

        Assert.True(watch.IsCutOff);
    }

    /// <summary>守 agnes 语义：见过 [DONE] 的正常收尾 + 思考-only，仍保留思考不重发</summary>
    [Fact]
    public void Thinking_WithTransportDone_IsNotCutOff()
    {
        StreamCompletionWatch watch = Watch(Update(new TextReasoningContent("想一想")));
        watch.MarkTransportEnd(sawDone: true);

        Assert.False(watch.IsCutOff);
    }

    /// <summary>finish_reason=length（max_tokens 截断）是合法收尾：无论 [DONE] 与否都不该重发</summary>
    [Fact]
    public void FinishReasonLength_WithTransportNoDone_IsNotCutOff()
    {
        StreamCompletionWatch watch = Watch(
            Update(new TextReasoningContent("想一想")), Finish(ChatFinishReason.Length));
        watch.MarkTransportEnd(sawDone: false);

        Assert.False(watch.IsCutOff);
        Assert.Equal("length", watch.FinishReason);
    }

    /// <summary>
    /// 「未见 [DONE] + 无 finish_reason → 一律重发」的代价显式钉住：正文完整但收尾帧被掐
    /// 同样重发（宁可白烧一次，不可静默收口）。这条同时封住工具调用半截的缝
    /// </summary>
    [Fact]
    public void Text_WithTransportNoDone_IsCutOff()
    {
        StreamCompletionWatch watch = Watch(Update(new TextContent("答案")));
        watch.MarkTransportEnd(sawDone: false);

        Assert.True(watch.IsCutOff);
    }

    /// <summary>传输层报告过 [DONE] 的正文收口不受影响</summary>
    [Fact]
    public void Text_WithTransportDone_IsNotCutOff()
    {
        StreamCompletionWatch watch = Watch(Update(new TextContent("答案")));
        watch.MarkTransportEnd(sawDone: true);

        Assert.False(watch.IsCutOff);
    }

    /// <summary>思考 + 正文混合、无 finish_reason：早就有正文了，当然不算截断</summary>
    [Fact]
    public void ReasoningAndText_WithoutFinishReason_IsNotCutOff() =>
        Assert.False(Watch(
            Update(new TextReasoningContent("想一想"), new TextContent("答案"))).IsCutOff);

    /// <summary>多个思考段（思考/正文交替）也一样：有产出就不是截断</summary>
    [Fact]
    public void MultiSegmentReasoning_WithoutFinishReason_IsNotCutOff() =>
        Assert.False(Watch(
            Update(new TextReasoningContent("第一段思考")),
            Update(new TextContent("正文")),
            Update(new TextReasoningContent("第二段思考"))).IsCutOff);

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
