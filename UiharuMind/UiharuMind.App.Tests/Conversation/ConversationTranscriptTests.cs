using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 转录器：AIContent 流 → 条目序列。这是流式渲染唯一的装配入口，
/// 以前埋在 1355 行的 ViewModel 里、只能靠实机验证。
/// </summary>
public class ConversationTranscriptTests
{
    private static (ConversationTranscript Transcript, List<ConversationItemBase> Items) Create(
        bool autoCollapseThinking = false, Action<string>? rememberShellPattern = null)
    {
        List<ConversationItemBase> items = new();
        // 与生产的 CreateAssistantItem() 一致：新气泡是「未完成」的，CloseSegment 才置为完成
        ConversationTranscript transcript = new(items, () => new TextConversationItem(false) { IsDone = false },
            rememberShellPattern)
        {
            AutoCollapseThinking = autoCollapseThinking,
        };
        return (transcript, items);
    }

    /// <summary>
    /// 流式期间 <c>Message</c> 是<b>节流</b>更新的（每个 token 都重设全文会带来一次
    /// 全量文本重排，成本随长度二次增长）。要读它的准确值必须先 <c>Flush</c>——
    /// 生产代码里由 <c>CloseSegment</c> 负责，收尾之前谁都不该拿它当同步真值。
    /// </summary>
    private static string FlushedMessage(ConversationItemBase item)
    {
        switch (item)
        {
            case TextConversationItem text:
                text.Flush();
                return text.Message;
            case ThinkingItem thinking:
                thinking.Flush();
                return thinking.Message;
            default:
                return item.Message;
        }
    }

    [Fact]
    public void TextDeltas_AccumulateIntoOneBubble()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextContent("你"));
        transcript.Apply(new TextContent("好"));
        transcript.Apply(new TextContent("世界"));

        ConversationItemBase item = Assert.Single(items);
        TextConversationItem text = Assert.IsType<TextConversationItem>(item);
        Assert.Equal("你好世界", FlushedMessage(text));
        Assert.False(text.IsDone); //未收尾
    }

    [Fact]
    public void CloseSegment_MarksBubbleDone_AndStartsNewOneAfterwards()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextContent("第一段"));
        transcript.CloseSegment();
        transcript.Apply(new TextContent("第二段"));

        Assert.Equal(2, items.Count);
        Assert.True(((TextConversationItem)items[0]).IsDone);
        Assert.Equal("第一段", FlushedMessage(items[0]));
        Assert.Equal("第二段", FlushedMessage(items[1]));
    }

    [Fact]
    public void EmptyText_ProducesNothing()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextContent(string.Empty));

        Assert.Empty(items);
    }

    [Fact]
    public void ReasoningContent_BecomesThinkingItem_ExpandedWhileStreaming()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextReasoningContent("先想一下"));

        ThinkingItem thinking = Assert.IsType<ThinkingItem>(Assert.Single(items));
        Assert.True(thinking.IsExpanded);
    }

    [Fact]
    public void ThinkingItem_CollapsesOnCloseWhenAutoCollapseOn()
    {
        var (transcript, items) = Create(autoCollapseThinking: true);

        transcript.Apply(new TextReasoningContent("想"));
        transcript.CloseSegment();

        Assert.False(((ThinkingItem)items[0]).IsExpanded);
    }

    [Fact]
    public void ThinkingItem_StaysExpandedWhenAutoCollapseOff()
    {
        var (transcript, items) = Create(autoCollapseThinking: false);

        transcript.Apply(new TextReasoningContent("想"));
        transcript.CloseSegment();

        Assert.True(((ThinkingItem)items[0]).IsExpanded);
    }

    /// <summary>
    /// 本地/部分远程模型把 &lt;think&gt; 混在正文流里，必须分离成独立的思考条目，
    /// 不能当正文渲染。
    /// </summary>
    [Fact]
    public void InlineThinkTag_SplitsIntoThinkingAndText()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextContent("<think>推理过程</think>正式回答"));
        transcript.CloseSegment();

        Assert.Equal(2, items.Count);
        Assert.IsType<ThinkingItem>(items[0]);
        Assert.Equal("推理过程", FlushedMessage(items[0]));
        Assert.Equal("正式回答", FlushedMessage(items[1]));
    }

    [Fact]
    public void InlineThinkTag_SplitAcrossDeltas()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextContent("<thi"));
        transcript.Apply(new TextContent("nk>分段推"));
        transcript.Apply(new TextContent("理</thi"));
        transcript.Apply(new TextContent("nk>答案"));
        transcript.CloseSegment();

        Assert.Equal("分段推理", FlushedMessage(items[0]));
        Assert.Equal("答案", FlushedMessage(items[1]));
    }

    [Fact]
    public void FunctionCall_BecomesToolCallItem_AndClosesCurrentSegment()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextContent("调用前的话"));
        transcript.Apply(new FunctionCallContent("call-1", "read_file",
            new Dictionary<string, object?> { ["path"] = "a.txt" }));

        Assert.Equal(2, items.Count);
        Assert.True(((TextConversationItem)items[0]).IsDone); //工具调用会收尾正文段
        ToolCallItem call = Assert.IsType<ToolCallItem>(items[1]);
        Assert.Equal("call-1", call.CallId);
        Assert.Equal("read_file", call.ToolName);
        Assert.True(call.IsRunning);
        Assert.Contains("path: a.txt", call.ArgumentsJson);
    }

    [Fact]
    public void FunctionResult_PairsBackToItsCallById()
    {
        var (transcript, items) = Create();

        transcript.Apply(new FunctionCallContent("a", "tool_a", null));
        transcript.Apply(new FunctionCallContent("b", "tool_b", null));
        transcript.Apply(new FunctionResultContent("b", "b 的结果"));

        ToolCallItem callA = items.OfType<ToolCallItem>().First(x => x.CallId == "a");
        ToolCallItem callB = items.OfType<ToolCallItem>().First(x => x.CallId == "b");
        Assert.True(callA.IsRunning); //没被误配
        Assert.False(callB.IsRunning);
        Assert.True(callB.IsSuccess);
        Assert.Equal("b 的结果", callB.ResultText);
    }

    [Fact]
    public void FunctionResult_WithException_MarksFailure()
    {
        var (transcript, items) = Create();

        transcript.Apply(new FunctionCallContent("a", "tool_a", null));
        transcript.Apply(new FunctionResultContent("a", null) { Exception = new InvalidOperationException("炸了") });

        ToolCallItem call = items.OfType<ToolCallItem>().Single();
        Assert.False(call.IsRunning);
        Assert.False(call.IsSuccess);
        Assert.Equal("炸了", call.ResultText);
    }

    [Fact]
    public void FunctionResult_WithUnknownCallId_IsIgnored()
    {
        var (transcript, items) = Create();

        transcript.Apply(new FunctionResultContent("不存在", "结果"));

        Assert.Empty(items);
    }

    /// <summary>
    /// 内务工具（todo / 模式切换）不渲染成条目，只发通知让调用方刷新面板
    /// </summary>
    [Theory]
    [InlineData("todos_write")]
    [InlineData("todos_read")]
    [InlineData("mode_set")]
    [InlineData("mode_get")]
    public void HousekeepingTool_RaisesEventInsteadOfRenderingItem(string toolName)
    {
        var (transcript, items) = Create();
        int raised = 0;
        transcript.HousekeepingToolCalled += () => raised++;

        transcript.Apply(new FunctionCallContent("x", toolName, null));

        Assert.Empty(items);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ErrorContent_BecomesErrorItem()
    {
        var (transcript, items) = Create();

        transcript.Apply(new ErrorContent("出错了"));

        Assert.Equal("出错了", Assert.IsType<ErrorItem>(Assert.Single(items)).Message);
    }

    /// <summary>
    /// 用量只经事件外发。回放实例不订阅，因此不会污染计数——
    /// 这取代了原先「按渲染落点判空」的隐式开关。
    /// </summary>
    [Fact]
    public void Usage_IsRaisedAsEvent_AndRendersNoItem()
    {
        var (transcript, items) = Create();
        List<UsageDetails> observed = new();
        transcript.UsageObserved += observed.Add;

        transcript.Apply(new UsageContent(new UsageDetails { InputTokenCount = 12, OutputTokenCount = 34 }));

        Assert.Empty(items);
        Assert.Equal(12, Assert.Single(observed).InputTokenCount);
    }

    [Fact]
    public void Usage_WithoutSubscriber_IsSilentlyDropped()
    {
        var (transcript, items) = Create();

        transcript.Apply(new UsageContent(new UsageDetails { InputTokenCount = 99 }));

        Assert.Empty(items);
    }

    [Fact]
    public void ApprovalRequest_IsPendingUntilResolved()
    {
        var (transcript, items) = Create();

        transcript.Apply(new ToolApprovalRequestContent("req-1",
            new FunctionCallContent("c1", "run_shell", new Dictionary<string, object?> { ["command"] = "ls" })));

        ApprovalRequestItem approval = Assert.IsType<ApprovalRequestItem>(Assert.Single(items));
        Assert.Single(transcript.PendingApprovals);
        Assert.Same(approval, transcript.PendingApprovals[0]);

        transcript.ResolveApprovals(new[] { approval });
        Assert.Empty(transcript.PendingApprovals);
    }

    /// <summary>
    /// 本轮审批被取走后仍留在待决清单里——用户在回应期间点「停止」必须还能取消它们
    /// </summary>
    [Fact]
    public void TakeRoundApprovals_EmptiesRoundButKeepsPending()
    {
        var (transcript, _) = Create();
        transcript.Apply(new ToolApprovalRequestContent("r", new FunctionCallContent("c", "run_shell", null)));

        IReadOnlyList<ApprovalRequestItem> round = transcript.TakeRoundApprovals();

        Assert.Single(round);
        Assert.Single(transcript.PendingApprovals);
        Assert.Empty(transcript.TakeRoundApprovals()); //第二次取为空
    }

    [Fact]
    public void CancelPendingApprovals_ResolvesThemAsDeny()
    {
        var (transcript, items) = Create();
        transcript.Apply(new ToolApprovalRequestContent("r", new FunctionCallContent("c", "run_shell", null)));

        transcript.CancelPendingApprovals();

        ApprovalRequestItem approval = (ApprovalRequestItem)items[0];
        Assert.True(approval.IsResolved);
        Assert.True(approval.Response.IsCompleted);
    }

    [Fact]
    public void RememberShellPattern_ReachesTheInjectedSink()
    {
        List<string> remembered = new();
        var (transcript, items) = Create(rememberShellPattern: remembered.Add);
        transcript.Apply(new ToolApprovalRequestContent("r",
            new FunctionCallContent("c", "run_shell", new Dictionary<string, object?> { ["command"] = "git status" })));

        ((ApprovalRequestItem)items[0]).RememberShellPatternCallback?.Invoke("git");

        Assert.Equal("git", Assert.Single(remembered));
    }

    /// <summary>
    /// 回放收尾：历史里的工具调用一律已结束，未回应的审批按拒绝处理，
    /// 否则切回旧会话会看到永远转圈的工具行和悬着的审批卡
    /// </summary>
    [Fact]
    public void FinalizeReplay_SettlesRunningCallsAndUnresolvedApprovals()
    {
        var (transcript, items) = Create();
        transcript.Apply(new FunctionCallContent("a", "tool_a", null));
        transcript.Apply(new ToolApprovalRequestContent("r", new FunctionCallContent("c", "run_shell", null)));
        transcript.Apply(new TextContent("尾巴"));

        transcript.FinalizeReplay("没有结果");

        ToolCallItem call = items.OfType<ToolCallItem>().Single();
        Assert.False(call.IsRunning);
        Assert.False(call.IsSuccess); //历史里没有配对结果 = 它没跑完,显示成绿色的成功态是谎报
        Assert.Equal("没有结果", call.ResultText);
        Assert.True(items.OfType<ApprovalRequestItem>().Single().IsResolved);
        Assert.True(items.OfType<TextConversationItem>().Single().IsDone);
    }

    /// <summary>
    /// 取消补写的工具结果要显示成失败。判据只能取正文——<c>FunctionResultContent.Exception</c>
    /// 带 <c>[JsonIgnore]</c>，存进会话文件再读回来就没了，卡片会重新变成绿色。
    /// </summary>
    [Fact]
    public void CancelledToolResult_ShowsAsFailed()
    {
        var (transcript, items) = Create();
        transcript.Apply(new FunctionCallContent("a", "ask_vision", null));
        transcript.Apply(new FunctionResultContent("a", ToolCallCancellation.ResultText));

        ToolCallItem call = items.OfType<ToolCallItem>().Single();
        Assert.False(call.IsRunning);
        Assert.False(call.IsSuccess);
    }

    [Fact]
    public void NormalToolResult_StaysSuccessful()
    {
        var (transcript, items) = Create();
        transcript.Apply(new FunctionCallContent("a", "run_shell", null));
        transcript.Apply(new FunctionResultContent("a", "ok"));

        Assert.True(items.OfType<ToolCallItem>().Single().IsSuccess);
    }

    [Fact]
    public void Reset_DropsStreamingStateSoNextTextStartsFresh()
    {
        var (transcript, items) = Create();
        transcript.Apply(new TextContent("旧"));

        transcript.Reset();
        transcript.Apply(new TextContent("新"));

        Assert.Equal(2, items.Count); //没有续写到旧气泡上
        Assert.Equal("旧", FlushedMessage(items[0]));
        Assert.Equal("新", FlushedMessage(items[1]));
        Assert.Empty(transcript.PendingApprovals);
    }

    /// <summary>
    /// 取消落库只该拿「正在流的那一段」。更早的段落已经由框架逐次服务调用各自落过盘
    /// （HarnessAgent 开着 RequirePerServiceCallChatHistoryPersistence），
    /// 再写一遍就会在会话里多出一句一模一样的话。
    /// </summary>
    [Fact]
    public void TakeStreamingText_ReturnsOnlyTheSegmentStillStreaming()
    {
        var (transcript, _) = Create();
        transcript.Apply(new TextContent("第一段"));
        //工具调用会收掉上一段:此后那一段已经随本次服务调用落过盘了
        transcript.Apply(new FunctionCallContent("call-1", "run_shell", null));
        transcript.Apply(new TextContent("第二段"));

        Assert.Equal("第二段", transcript.TakeStreamingText());
    }

    [Fact]
    public void TakeStreamingText_IsNullWhenStoppedOnAToolCall()
    {
        var (transcript, _) = Create();
        transcript.Apply(new TextContent("第一段"));
        transcript.Apply(new FunctionCallContent("call-1", "ask_vision", null));

        //卡在工具调用上停止:没有任何在流的正文,一个字都不该落库
        Assert.Null(transcript.TakeStreamingText());
    }

    [Fact]
    public void StopRunningToolCalls_ClosesUnfinishedCardsAsFailed()
    {
        var (transcript, items) = Create();
        transcript.Apply(new FunctionCallContent("done", "run_shell", null));
        transcript.Apply(new FunctionResultContent("done", "ok"));
        transcript.Apply(new FunctionCallContent("running", "ask_vision", null));

        transcript.StopRunningToolCalls("已停止");

        ToolCallItem finished = items.OfType<ToolCallItem>().Single(x => x.CallId == "done");
        ToolCallItem stopped = items.OfType<ToolCallItem>().Single(x => x.CallId == "running");
        Assert.True(finished.IsSuccess); //已经拿到结果的卡片不该被改判
        Assert.Equal("ok", finished.ResultText);
        Assert.False(stopped.IsRunning); //不收的话卡片会一直转圈,看着像还在跑
        Assert.False(stopped.IsSuccess); //它确实没跑完,绿点会是假消息
        Assert.Equal("已停止", stopped.ResultText);
    }

    [Fact]
    public void StopRunningToolCalls_ReachesNestedActivity()
    {
        var (transcript, items) = Create();
        transcript.Apply(new FunctionCallContent("outer", "sub_agent", null));
        //子代理过程里自己又调了一个工具,同样停在半路
        transcript.Apply(new ToolActivityContent("outer", new FunctionCallContent("inner", "run_shell", null)));

        transcript.StopRunningToolCalls("已停止");

        ToolCallItem outer = items.OfType<ToolCallItem>().Single();
        ToolCallItem inner = outer.NestedItems.OfType<ToolCallItem>().Single();
        Assert.False(outer.IsRunning);
        Assert.False(inner.IsRunning);
    }

    /// <summary>
    /// 实时流与回放是同一个类的两个实例，落点不同、互不干扰
    /// </summary>
    [Fact]
    public void TwoInstances_WriteToTheirOwnTargets()
    {
        List<ConversationItemBase> live = new();
        List<ConversationItemBase> replay = new();
        ConversationTranscript liveTranscript = new(live, () => new TextConversationItem(false));
        ConversationTranscript replayTranscript = new(replay, () => new TextConversationItem(false));

        liveTranscript.Apply(new TextContent("实时"));
        replayTranscript.Apply(new TextContent("回放"));

        Assert.Equal("实时", FlushedMessage(Assert.Single(live)));
        Assert.Equal("回放", FlushedMessage(Assert.Single(replay)));
    }
}
