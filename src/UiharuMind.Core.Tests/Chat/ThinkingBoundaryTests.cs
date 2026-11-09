using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
// 项目自有的 struct ChatMessage 遮蔽了 Microsoft.Extensions.AI.ChatMessage，阶段 2 会删除它
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 思考边界分发的承重假设：转录器（渲染）、计时器、快捷窗口、存档回放与搜索共用
/// <see cref="ThinkingBoundary"/>，新内容类型只在这里表态一次——改了不生效等于静默分叉。
/// 分类只认 AIContent 类型，任何「看文本猜这是不是思考」的启发式都会吞掉正文。
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

    /// <summary>
    /// 框架给出结构化思考后，同一段正文里的标签只能是引用，整段按正文放行。
    /// </summary>
    [Fact]
    public void Dispatch_StructuredReasoningDisablesInlineTags()
    {
        ThinkTagStreamParser parser = NewParser();
        List<string> texts = new();
        List<string> thinking = new();

        ThinkingBoundary.Dispatch(new TextReasoningContent("想"), parser, texts.Add, thinking.Add,
            () => Assert.Fail("思考不应终结思考段"));
        ThinkingBoundary.Dispatch(new TextContent("<think>引用</think>回答"), parser, texts.Add, thinking.Add,
            () => Assert.Fail("正文不应终结思考段"));

        Assert.Equal(["想"], thinking);
        Assert.Equal("<think>引用</think>回答", string.Concat(texts));
    }

    /// <summary>
    /// 空思考与只带密文的加密思考不代表「框架已用文本表达思考」：它们不改变解析器状态，
    /// 否则本地内联模型的一条空占位就会让后面的真标签失效。
    /// </summary>
    [Fact]
    public void Dispatch_EmptyReasoningLeavesInlineTagsEnabled()
    {
        ThinkTagStreamParser parser = NewParser();
        List<string> texts = new();
        List<string> thinking = new();

        ThinkingBoundary.Dispatch(new TextReasoningContent(string.Empty) { ProtectedData = "BLOB" },
            parser, texts.Add, thinking.Add, () => Assert.Fail("空思考不应收口"));
        ThinkingBoundary.Dispatch(new TextContent("<think>再想</think>结论"), parser, texts.Add, thinking.Add,
            () => Assert.Fail("正文不应终结思考段"));

        Assert.Equal(["再想"], thinking);
        Assert.Equal("结论", string.Concat(texts));
    }

    /// <summary>
    /// 思考里提到未配对的 &lt;think&gt; 字面量，不能把整段回答吞进思考卡。
    /// 实机事故：一份总结报告被整段折叠进思考卡，界面上只剩「只有思考」。
    /// </summary>
    [Fact]
    public void ReasoningMentionsThinkTag_KeepsWholeAnswerVisible()
    {
        ThinkTagStreamParser parser = NewParser();
        List<string> texts = new();
        List<string> thinking = new();
        const string answer = "## 总结报告\n一、结论……\n二、后续……";
        const string reasoning = "用户问的是 <think> 标签该怎么解析";

        ThinkingBoundary.Dispatch(new TextReasoningContent(reasoning), parser,
            texts.Add, thinking.Add, () => Assert.Fail("思考不应终结思考段"));
        ThinkingBoundary.Dispatch(new TextContent(answer), parser,
            texts.Add, thinking.Add, () => Assert.Fail("正文不应终结思考段"));

        Assert.Equal(answer, string.Concat(texts));
        Assert.Equal([reasoning], thinking);
    }

    /// <summary>思考里一个孤立引号、正文以引号起头，同样不能触发「续段」猜测</summary>
    [Fact]
    public void OddQuoteInReasoning_KeepsQuotedAnswerVisible()
    {
        ThinkTagStreamParser parser = NewParser();
        List<string> texts = new();
        List<string> thinking = new();
        const string answer = "\"好的\"，这是回答的开头";

        ThinkingBoundary.Dispatch(new TextReasoningContent("看一下源码里 \" 这个引号的用法"), parser,
            texts.Add, thinking.Add, () => Assert.Fail("思考不应终结思考段"));
        ThinkingBoundary.Dispatch(new TextContent(answer), parser,
            texts.Add, thinking.Add, () => Assert.Fail("正文不应终结思考段"));

        Assert.Equal(answer, string.Concat(texts));
        Assert.Single(thinking);
    }

    /// <summary>
    /// 段边界只收段：工具调用之后，本发「已给出结构化思考」这个事实不能被抹掉，
    /// 否则回答里引用的 &lt;think&gt; 又会被当真标签把正文切进思考卡。
    /// </summary>
    [Fact]
    public void StructuredReasoningSurvivesSegmentBoundary()
    {
        ThinkTagStreamParser parser = NewParser();
        List<string> texts = new();
        List<string> thinking = new();
        const string answer = "<think> 是标签字面量，回答继续";

        ThinkingBoundary.Dispatch(new TextReasoningContent("先想"), parser,
            texts.Add, thinking.Add, () => { });
        ThinkingBoundary.Dispatch(new FunctionCallContent("call-1", "Read"), parser,
            texts.Add, thinking.Add, () => { });
        ThinkingBoundary.Dispatch(new TextContent(answer), parser,
            texts.Add, thinking.Add, () => { });

        Assert.Equal(answer, string.Concat(texts));
        Assert.Equal(["先想"], thinking);
    }

    /// <summary>
    /// 迟到的落盘信号同样不能复位开关：下一发的结构化思考可能只有一帧，边界之后
    /// 不会再有 reasoning 增量把开关关回去。
    /// </summary>
    [Fact]
    public void LateMessageBoundaryDoesNotReenableInlineTags()
    {
        ThinkTagStreamParser parser = NewParser();
        List<string> texts = new();
        List<string> thinking = new();
        const string answer = "<think>标签用法</think>答案";

        ThinkingBoundary.Dispatch(new TextReasoningContent("R"), parser,
            texts.Add, thinking.Add, () => { });
        ThinkingBoundary.Dispatch(MessageBoundaryContent.Instance, parser,
            texts.Add, thinking.Add, () => { });
        ThinkingBoundary.Dispatch(new TextContent(answer), parser,
            texts.Add, thinking.Add, () => { });

        Assert.Equal(answer, string.Concat(texts));
        Assert.Equal(["R"], thinking);
    }

    /// <summary>
    /// 新的一发开始必须完整复位：从结构化思考模型切到本地内联模型后，
    /// 段首的 &lt;think&gt; 要重新算标签，否则思考永远不折叠、界面与存档分叉。
    /// </summary>
    [Fact]
    public void ServiceCallStarted_ReenablesInlineTagsForTheNextCall()
    {
        ThinkTagStreamParser parser = NewParser();
        List<string> texts = new();
        List<string> thinking = new();

        ThinkingBoundary.Dispatch(new TextReasoningContent("想"), parser,
            texts.Add, thinking.Add, () => { });
        ThinkingBoundary.Dispatch(new TextContent("<think>引用</think>回答"), parser,
            texts.Add, thinking.Add, () => { });
        ThinkingBoundary.Dispatch(MessageBoundaryContent.Instance, parser,
            texts.Add, thinking.Add, () => { });
        ThinkingBoundary.Dispatch(ServiceCallStartedContent.Instance, parser,
            texts.Add, thinking.Add, () => { });
        ThinkingBoundary.Dispatch(new TextContent("<think>再想</think>结论"), parser,
            texts.Add, thinking.Add, () => { });

        Assert.Equal(["想", "再想"], thinking);
        Assert.Equal("<think>引用</think>回答结论", string.Concat(texts));
    }

    /// <summary>本地内联模型没有结构化思考，跨段边界仍要认段首的 &lt;think&gt;</summary>
    [Fact]
    public void InlineModelStillParsesTagsAfterSegmentBoundary()
    {
        ThinkTagStreamParser parser = NewParser();
        List<string> texts = new();
        List<string> thinking = new();

        ThinkingBoundary.Dispatch(new TextContent("<think>第一发</think>正文"), parser,
            texts.Add, thinking.Add, () => { });
        ThinkingBoundary.Dispatch(MessageBoundaryContent.Instance, parser,
            texts.Add, thinking.Add, () => { });
        ThinkingBoundary.Dispatch(new TextContent("<think>第二发</think>结论"), parser,
            texts.Add, thinking.Add, () => { });

        Assert.Equal("正文结论", string.Concat(texts));
        Assert.Equal(["第一发", "第二发"], thinking);
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
        // 一次服务调用收口，与工具调用、审批同属唯一名单
        MessageBoundaryContent.Instance,
        // 发请求前的可靠新发起点，同样收口
        ServiceCallStartedContent.Instance,
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
