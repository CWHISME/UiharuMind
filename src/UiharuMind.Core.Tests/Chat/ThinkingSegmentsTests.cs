using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
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

    /// <summary>消息开头混在正文里的思考仍然切段（本地模型那条路不能一起关死）</summary>
    [Fact]
    public void ThinkBlockAtMessageStart_IsStillASegment()
    {
        ChatMessage message = new(ChatRole.Assistant, [new TextContent("<think>推理</think>正文")]);

        Assert.Equal([2], ThinkingSegments.LengthsOf(message));
    }
}
