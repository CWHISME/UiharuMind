using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
// 项目自有的 struct ChatMessage 遮蔽了 Microsoft.Extensions.AI.ChatMessage，阶段 2 会删除它
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 存档侧的思考段口径（<see cref="ThinkingSegments"/>）：落盘字数、卡片长度、回放认领都从这一处取。
///
/// 事故：回答正文里引用了字面量 &lt;think&gt;，从前会被当成真的开标签切出一段假思考，
/// 字数跟着计进存档（766 + 4784 = 5550），界面上那张卡顶着的其实是回答的后半截。
/// </summary>
public class ThinkingSegmentsTests
{
    /// <summary>正文里引用的标签不切段：只有结构化思考那一段</summary>
    [Fact]
    public void QuotedThinkTagInBody_IsNotASegment()
    {
        string reasoning = new('想', 40);
        ChatMessage message = new(ChatRole.Assistant,
        [
            new TextReasoningContent(reasoning),
            new TextContent("实测（400 条 <think> 混排：1.7 ms）"),
        ]);

        Assert.Equal([40], ThinkingSegments.LengthsOf(message));
    }

    /// <summary>
    /// 已有结构化思考时，正文<b>开头</b>的标签字面量也不该切出假段。
    /// 只有正文先上屏才按字面量的旧规则挡不住这种形状，回放会把引用算进存档字数。
    /// </summary>
    [Fact]
    public void StructuredReasoningThenTagLiteralAtBodyStart_IsNotASegment()
    {
        string reasoning = new('想', 40);
        ChatMessage message = new(ChatRole.Assistant,
        [
            new TextReasoningContent(reasoning),
            new TextContent("<think> 这类引用出现在回答开头"),
        ]);

        Assert.Equal([40], ThinkingSegments.LengthsOf(message));
    }

    /// <summary>
    /// 被上游切开标成正文的推理续句按框架类型算正文，存档侧与界面卡片同口径。
    /// 猜「这是不是思考续段」的代价是整段回答被折叠进思考卡，按类型走只是续句显示在正文里，内容不丢。
    /// </summary>
    [Fact]
    public void MislabeledReasoningTail_IsNotAThinkingSegment()
    {
        const string head = "考虑一段文本 \"hello ";
        const string tail = "\". 后续分析仍然是推理的一部分";
        ChatMessage message = new(ChatRole.Assistant,
        [
            new TextReasoningContent(head),
            new TextContent(tail),
            new FunctionCallContent("call-1", "Read"),
        ]);

        Assert.Equal([head.Length], ThinkingSegments.LengthsOf(message));
    }

    /// <summary>落盘信号在统一边界名单里；万一混进历史，两段思考仍要分开</summary>
    [Fact]
    public void MessageBoundaryInsideMessage_StillSplitsSegments()
    {
        ChatMessage message = new(ChatRole.Assistant,
        [
            new TextReasoningContent("甲"),
            MessageBoundaryContent.Instance,
            new TextReasoningContent("乙"),
        ]);

        Assert.Equal([1, 1], ThinkingSegments.LengthsOf(message));
    }

    /// <summary>消息开头混在正文里的思考仍然切段（本地模型那条路不能一起关死）</summary>
    [Fact]
    public void ThinkBlockAtMessageStart_IsStillASegment()
    {
        ChatMessage message = new(ChatRole.Assistant, [new TextContent("<think>推理</think>正文")]);

        Assert.Equal([2], ThinkingSegments.LengthsOf(message));
    }
}
