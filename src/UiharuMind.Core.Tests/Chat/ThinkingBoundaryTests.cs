using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
// 项目自有的 struct ChatMessage 遮蔽了 Microsoft.Extensions.AI.ChatMessage，阶段 2 会删除它
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 思考边界分发的承重假设：转录器（渲染）、计时器（落盘）、快捷窗口三处共用
/// <see cref="ThinkingBoundary"/>，新内容类型只在这里表态一次——改了不生效等于静默分叉。
/// </summary>
public class ThinkingBoundaryTests
{
    [Fact]
    public void Dispatch_ReasoningGoesToThinking_EmptyIgnored()
    {
        List<string> thinking = new();
        int breaks = 0;

        ThinkingBoundary.Dispatch(new TextReasoningContent("想。"), NewParser(), _ => { }, thinking.Add, () => breaks++);
        ThinkingBoundary.Dispatch(new TextReasoningContent(string.Empty), NewParser(), _ => { }, thinking.Add, () => breaks++);

        Assert.Equal(["想。"], thinking);
        Assert.Equal(0, breaks);
    }

    [Fact]
    public void Dispatch_TextSplitsThinkTags()
    {
        List<string> texts = new();
        List<string> thinking = new();

        ThinkingBoundary.Dispatch(new TextContent("<think>推理</think>回答"), NewParser(), texts.Add, thinking.Add, () => Assert.Fail("正文不应终结思考段"));

        Assert.Equal(["推理"], thinking);
        Assert.Equal(["回答"], texts);
    }

    [Theory]
    [MemberData(nameof(BreakContents))]
    public void Dispatch_BreakContents_CallOnBreak(AIContent content)
    {
        int breaks = 0;

        ThinkingBoundary.Dispatch(content, NewParser(), _ => Assert.Fail("终结段不应产出正文"),
            _ => Assert.Fail("终结段不应产出思考"), () => breaks++);

        Assert.Equal(1, breaks);
    }

    public static TheoryData<AIContent> BreakContents => new()
    {
        new FunctionCallContent("call-1", "run_shell"),
        MessageBoundaryContent.Instance,
        new ToolApprovalRequestContent("approval-1",
            new FunctionCallContent("call-1", "run_shell")),
        new UserMessageContent(new ChatMessage(ChatRole.User, "嗨")),
    };

    [Theory]
    [MemberData(nameof(NeutralContents))]
    public void Dispatch_NeutralContents_CallNothing(AIContent content)
    {
        ThinkingBoundary.Dispatch(content, NewParser(), _ => Assert.Fail("无关内容不应产出正文"),
            _ => Assert.Fail("无关内容不应产出思考"), () => Assert.Fail("无关内容不应终结思考段"));
    }

    public static TheoryData<AIContent> NeutralContents => new()
    {
        new FunctionResultContent("call-1", "ok"),
        new TextContent(string.Empty),
    };

    [Fact]
    public void Complete_FlushesHeldPartialTag()
    {
        // 流式切碎的半截标签扣在 parser 里，收段时才定夺——计时器与转录器必须同点 flush，
        // 否则两边的 parser 跨段错位
        ThinkTagStreamParser parser = NewParser();
        List<string> thinking = new();
        List<string> texts = new();
        ThinkingBoundary.Dispatch(new TextContent("<thi"), parser, texts.Add, thinking.Add, () => Assert.Fail("扣留期不应有输出"));

        Assert.Empty(thinking);
        Assert.Empty(texts);

        int breaks = 0;
        ThinkingBoundary.Complete(parser, texts.Add, thinking.Add, () => breaks++);

        Assert.Equal(1, breaks);
        Assert.Single(texts); //疑似半个标签按正文原样放出
    }

    private static ThinkTagStreamParser NewParser() => new();
}
