using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
// 项目自有的 struct ChatMessage 遮蔽了 Microsoft.Extensions.AI.ChatMessage
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 计时器（流式）与存档回放必须对同一串内容量出同样的思考字数：两边共用
/// <see cref="ThinkingBoundary.Dispatch"/>，任一侧自立规则都会让盖章的段与界面卡片对不上。
/// </summary>
public class ThinkingPipelineConsistencyTests
{
    [Fact]
    public void StatsRecorderAndArchivedSegments_AgreeOnStructuredReasoning()
    {
        AIContent[] contents =
        [
            new TextReasoningContent("先想"),
            new TextContent("<think> 是引用，回答继续"),
            MessageBoundaryContent.Instance,
            new TextReasoningContent("再想"),
            new TextContent("结论"),
        ];

        ThinkingStatsRecorder recorder = new();
        foreach (AIContent content in contents) recorder.NoteContent(content);

        ChatMessage message = new(ChatRole.Assistant, contents);
        Assert.Equal(recorder.TotalThinkingChars, ThinkingSegments.TotalLengthOf(message));
    }

    [Fact]
    public void StatsRecorderAndArchivedSegments_AgreeOnInlineThink()
    {
        AIContent[] contents = [new TextContent("<think>内联推理</think>结论")];

        ThinkingStatsRecorder recorder = new();
        foreach (AIContent content in contents) recorder.NoteContent(content);

        ChatMessage message = new(ChatRole.Assistant, contents);
        Assert.Equal(recorder.TotalThinkingChars, ThinkingSegments.TotalLengthOf(message));
    }
}
