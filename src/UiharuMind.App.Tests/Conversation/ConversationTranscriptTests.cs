using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Shared.Utils.Tools;

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

    //================= 执行者给出的边界 =================

    /// <summary>
    /// 一轮之内两次服务调用的正文在流里是<b>连着</b>的（插话之后那次调用正是"文本接文本"），
    /// 靠工具调用或 think/text 切换猜不出边界。执行者在落盘那一刻发边界，这里照着收段。
    /// </summary>
    [Fact]
    public void MessageBoundary_SplitsConsecutiveTextIntoTwoBubbles()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextContent("第一次调用的回复"));
        transcript.Apply(MessageBoundaryContent.Instance);
        transcript.Apply(new TextContent("第二次调用的回复"));

        Assert.Equal(2, items.Count);
        Assert.True(((TextConversationItem)items[0]).IsDone);
        Assert.Equal("第一次调用的回复", FlushedMessage(items[0]));
        Assert.Equal("第二次调用的回复", FlushedMessage(items[1]));
    }

    private static (ConversationTranscript Transcript, List<ConversationItemBase> Items) CreateWithUserFactory(
        IReadOnlyList<ConversationItemBase>? renderedBefore = null)
    {
        List<ConversationItemBase> items = new();
        ConversationTranscript transcript = new(items, () => new TextConversationItem(false) { IsDone = false },
            renderedBefore: renderedBefore,
            createUserItem: message => new TextConversationItem(true) { Message = message.Text, SourceMessage = message });
        return (transcript, items);
    }

    /// <summary>被消费的用户消息画在<b>此刻</b>的位置：前面的回复收段，后面的回复另起气泡</summary>
    [Fact]
    public void UserMessage_IsRenderedWhereItWasConsumed()
    {
        var (transcript, items) = CreateWithUserFactory();
        ChatMessage interjection = new(ChatRole.User, "666");

        transcript.Apply(new TextContent("先答一半"));
        transcript.Apply(new UserMessageContent(interjection, isInterjection: true));
        transcript.Apply(new TextContent("再答插话"));

        Assert.Equal(3, items.Count);
        Assert.True(((TextConversationItem)items[0]).IsDone);
        TextConversationItem user = Assert.IsType<TextConversationItem>(items[1]);
        Assert.True(user.IsUser);
        Assert.Same(interjection, user.SourceMessage);
        Assert.Equal("再答插话", FlushedMessage(items[2]));
    }

    /// <summary>
    /// 发送方那一格发送时已经画过同一个实例（乐观显示），流里再来一次不能画第二遍。
    /// 按<b>引用</b>认，不按正文——正文相同的两句是两条消息。
    /// </summary>
    [Fact]
    public void UserMessage_AlreadyShownForTheSameInstance_IsNotDrawnTwice()
    {
        var (transcript, items) = CreateWithUserFactory();
        ChatMessage prompt = new(ChatRole.User, "开工");
        ChatMessage sameTextOtherMessage = new(ChatRole.User, "开工");
        items.Add(new TextConversationItem(true) { Message = "开工", SourceMessage = prompt });

        transcript.Apply(new UserMessageContent(prompt));
        transcript.Apply(new UserMessageContent(sameTextOtherMessage));

        Assert.Equal(2, items.Count);
        Assert.Same(sameTextOtherMessage, items[1].SourceMessage);
    }

    [Fact]
    public void UserMessage_ShownInAnEarlierBatch_IsRecognisedThroughRenderedBefore()
    {
        ChatMessage prompt = new(ChatRole.User, "开工");
        List<ConversationItemBase> earlier = [new TextConversationItem(true) { Message = "开工", SourceMessage = prompt }];
        var (transcript, items) = CreateWithUserFactory(earlier);

        transcript.Apply(new UserMessageContent(prompt));

        Assert.Empty(items);
    }

    [Fact]
    public void UserMessage_WithoutAFactory_IsIgnored()
    {
        var (transcript, items) = Create();

        transcript.Apply(new UserMessageContent(new ChatMessage(ChatRole.User, "开工")));

        Assert.Empty(items);
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
    public void ThinkingItem_Flush_PublishesDurationAndCharCountStats()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextReasoningContent("推理内容"));
        ThinkingItem thinking = Assert.IsType<ThinkingItem>(Assert.Single(items));

        // 流式进行中统计标签不该先行出现(节拍泵没跑,标题栏保持干净)
        Assert.Equal(string.Empty, thinking.StatsText);

        transcript.CloseSegment();

        Assert.Equal("推理内容", FlushedMessage(thinking));
        Assert.False(string.IsNullOrEmpty(thinking.StatsText));
    }

    /// <summary>
    /// 截断预览锚在头部而不是尾部：起点钉在 0、内容不变，卡片高度才不随节拍跳。
    /// 截断时标记 IsPreviewTruncated，卡片上据此显示「查看全文」按钮。
    /// </summary>
    [Fact]
    public void ThinkingItem_TruncatedPreview_AnchorsHeadAndFlagsButton()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextReasoningContent(new string('思', 2000)));
        ThinkingItem thinking = Assert.IsType<ThinkingItem>(Assert.Single(items));

        ((IStreamFlushTarget)thinking).FlushForDisplay();

        Assert.True(thinking.IsPreviewTruncated);
        Assert.StartsWith("思", thinking.Message); //头部锚定
        Assert.Contains("…", thinking.Message); //截断提示

        transcript.CloseSegment();

        // 收尾也走同一套截断:超限仍保留「查看全文」入口,Message 是截断预览而非全文
        Assert.True(thinking.IsPreviewTruncated);
        Assert.StartsWith("思", thinking.Message);
        Assert.Contains("…", thinking.Message);
    }

    /// <summary>订阅返回订阅时刻的全量快照，后续增量走回调</summary>
    [Fact]
    public void SubscribeContent_ReturnsSnapshotAndPushesDeltas()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextReasoningContent("开头"));
        ThinkingItem thinking = Assert.IsType<ThinkingItem>(Assert.Single(items));

        var received = new List<string>();
        string snapshot = thinking.SubscribeContent(received.Add);
        Assert.Equal("开头", snapshot);

        transcript.Apply(new TextReasoningContent("中间"));
        transcript.Apply(new TextReasoningContent("结尾"));
        transcript.CloseSegment(); //收尾 Flush,把订阅后的增量一次性推给订阅者

        Assert.Equal("中间结尾", string.Concat(received));
    }

    /// <summary>多个订阅者各记各的游标,后开的窗口不干扰先开的</summary>
    [Fact]
    public void SubscribeContent_MultipleSubscribers_EachGetsOwnDelta()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextReasoningContent("一"));
        ThinkingItem thinking = Assert.IsType<ThinkingItem>(Assert.Single(items));

        var first = new List<string>();
        thinking.SubscribeContent(first.Add);

        transcript.Apply(new TextReasoningContent("二"));

        var second = new List<string>();
        thinking.SubscribeContent(second.Add);

        transcript.CloseSegment();

        // first 游标停在「一」之后,只该拿到「二」;second 订阅时「二」已在快照之外,
        // 但订阅后没有新内容,所以拿不到任何增量
        Assert.Equal("二", string.Concat(first));
        Assert.Equal(string.Empty, string.Concat(second));
    }

    /// <summary>退订后不再收到增量</summary>
    [Fact]
    public void UnsubscribeContent_StopsDeliveries()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextReasoningContent("一"));
        ThinkingItem thinking = Assert.IsType<ThinkingItem>(Assert.Single(items));

        var received = new List<string>();
        thinking.SubscribeContent(received.Add);
        thinking.UnsubscribeContent(received.Add);

        transcript.Apply(new TextReasoningContent("二"));
        transcript.CloseSegment();

        Assert.Empty(received);
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

    /// <summary>
    /// 思考段与正文段互斥。条目按到达顺序进集合，正在流的那条气泡排在后来的思考卡<b>前面</b>
    /// ——不收尾的话，思考之后的正文会续进那条气泡里，界面上就成了「回答在思考过程上面」，
    /// 而历史里它们是两条消息，重开会话立刻对不上（子会话窗口那个「实时看是一坨、
    /// 重开就分开了」正是它）。
    /// </summary>
    [Fact]
    public void TextAfterThinking_StartsANewBubble()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextContent("第一段回答"));
        transcript.Apply(new TextReasoningContent("再想想"));
        transcript.Apply(new TextContent("第二段回答"));
        transcript.CloseSegment();

        Assert.Equal(3, items.Count);
        Assert.Equal("第一段回答", FlushedMessage(items[0]));
        Assert.IsType<ThinkingItem>(items[1]);
        Assert.Equal("第二段回答", FlushedMessage(items[2]));
        Assert.True(((TextConversationItem)items[0]).IsDone); //上一段已经收尾,不会再往里追加
    }

    /// <summary>
    /// 反向同理：正文开始就把思考段收掉，否则之后的推理会续进正文<b>上面</b>那张思考卡
    /// </summary>
    [Fact]
    public void ThinkingAfterText_StartsANewThinkingCard()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextReasoningContent("先想"));
        transcript.Apply(new TextContent("说一句"));
        transcript.Apply(new TextReasoningContent("再想"));
        transcript.CloseSegment();

        Assert.Equal(3, items.Count);
        Assert.Equal("先想", FlushedMessage(items[0]));
        Assert.Equal("说一句", FlushedMessage(items[1]));
        Assert.Equal("再想", FlushedMessage(items[2]));
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
    public void WhitespaceTextBeforeToolCall_LeavesNoEmptyBubble()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextContent("\n\n"));
        transcript.Apply(new FunctionCallContent("call-1", "read_file",
            new Dictionary<string, object?> { ["path"] = "a.txt" }));

        ToolCallItem call = Assert.IsType<ToolCallItem>(Assert.Single(items));
        Assert.Equal("call-1", call.CallId);
    }

    [Fact]
    public void WhitespaceTextBetweenToolCalls_LeavesOnlyToolCards()
    {
        var (transcript, items) = Create();

        transcript.Apply(new FunctionCallContent("a", "tool_a", null));
        transcript.Apply(new TextContent("  \n "));
        transcript.Apply(new FunctionCallContent("b", "tool_b", null));

        Assert.Equal(2, items.Count);
        Assert.All(items, x => Assert.IsType<ToolCallItem>(x));
    }

    [Fact]
    public void TextWithRealContentAroundWhitespace_IsKept()
    {
        var (transcript, items) = Create();

        transcript.Apply(new TextContent("\n\n"));
        transcript.Apply(new TextContent("有实质内容"));
        transcript.CloseSegment();

        TextConversationItem text = Assert.IsType<TextConversationItem>(Assert.Single(items));
        Assert.Equal("有实质内容", FlushedMessage(text));
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

    /// <summary>用量不进条目流——它走 ETurnNotice 通知链路，转录器只负责忽略。</summary>
    [Fact]
    public void Usage_RendersNoItem()
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
        ToolApprovalRequestContent request = new("r", new FunctionCallContent("c", "run_shell", null));
        transcript.Apply(request);

        IReadOnlyList<ApprovalRequestItem> round = transcript.TakeRoundApprovals([request]);

        Assert.Single(round);
        Assert.Single(transcript.PendingApprovals);
        Assert.Empty(transcript.TakeRoundApprovals([request])); //第二次取为空
    }

    /// <summary>
    /// 只领自己那一批：同一会话上可能同时有两轮在跑（用户那一轮与后台委派回来时起的唤醒轮
    /// 共用这一个转录器）。无脑抽干会把别人那一轮的卡片领走，让那一轮的工具调用
    /// 永远没有结果地留在历史里——实机踩到过，所以钉住
    /// </summary>
    [Fact]
    public void TakeRoundApprovals_LeavesOtherTurnsRequestsAlone()
    {
        var (transcript, _) = Create();
        ToolApprovalRequestContent mine = new("r1", new FunctionCallContent("c1", "run_shell", null));
        ToolApprovalRequestContent theirs = new("r2", new FunctionCallContent("c2", "write_file", null));
        transcript.Apply(mine);
        transcript.Apply(theirs);

        IReadOnlyList<ApprovalRequestItem> round = transcript.TakeRoundApprovals([mine]);

        Assert.Same(mine, Assert.Single(round).Request);
        //别人那一批还在,轮得到它自己来取
        Assert.Same(theirs, Assert.Single(transcript.TakeRoundApprovals([theirs])).Request);
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
    /// 收口过的卡再收到结果要被覆盖，而不是停在「历史里没有这次调用的结果」。
    /// </summary>
    [Fact]
    public void ResultAppliedAfterFinalizeReplay_OverwritesTheUnfinishedNote()
    {
        var (transcript, items) = Create();
        transcript.Apply(new FunctionCallContent("a", "Write", null));
        transcript.FinalizeReplay("历史里没有这次调用的结果");
        Assert.Equal("历史里没有这次调用的结果", items.OfType<ToolCallItem>().Single().ResultText);

        transcript.Apply(new FunctionResultContent("a", ToolCallCancellation.ApprovalUnansweredResultText));

        ToolCallItem call = items.OfType<ToolCallItem>().Single();
        Assert.Single(items.OfType<ToolCallItem>()); //回写不造新卡
        Assert.False(call.IsRunning);
        Assert.False(call.IsSuccess); //[cancelled] 显示成失败
        Assert.Equal(ToolCallCancellation.ApprovalUnansweredResultText, call.ResultText);
    }

    /// <summary>
    /// 开窗分批回放把调用与结果切在两批里：批内看不到结果，收尾不能把这张卡收口成
    /// 「历史里没有这次调用的结果」——盘上有结果却这么显示就是谎报。实机撞到过
    /// （子会话 6b09cf4c：首屏是 12–16，补窗是 7–11，第 11 条的 Write 结果落在第 12 条）。
    /// </summary>
    [Fact]
    public void ResultInTheNextWindow_IsWrittenBackBeforeFinalize()
    {
        var (transcript, items) = Create();
        List<ChatMessage> history =
        [
            new(ChatRole.Assistant, [new FunctionCallContent("a", "Write", null)]),
            new(ChatRole.Tool, [new FunctionResultContent("a", "Nobody answered this approval")]),
        ];

        // 本批只有第 0 条(调用),结果在批外
        transcript.Apply(history[0].Contents[0]);
        transcript.ApplyLaterResults(history, 1);
        transcript.FinalizeReplay("历史里没有这次调用的结果");

        ToolCallItem call = Assert.Single(items.OfType<ToolCallItem>());
        Assert.False(call.IsRunning);
        Assert.Equal("Nobody answered this approval", call.ResultText);
    }

    /// <summary>
    /// 批外真的没有结果时，收尾照旧收口——回写不能顺手把「没有结果」也抹掉。
    /// </summary>
    [Fact]
    public void NoResultAnywhere_StillClosesAsUnfinished()
    {
        var (transcript, items) = Create();
        List<ChatMessage> history = [new(ChatRole.Assistant, [new FunctionCallContent("a", "Write", null)])];

        transcript.Apply(history[0].Contents[0]);
        transcript.ApplyLaterResults(history, 1);
        transcript.FinalizeReplay("历史里没有这次调用的结果");

        Assert.Equal("历史里没有这次调用的结果", items.OfType<ToolCallItem>().Single().ResultText);
    }

    /// <summary>
    /// 取消补写的工具结果要显示成失败。判据只能取正文——<c>FunctionResultContent.Exception</c>
    /// 带 <c>[JsonIgnore]</c>，存进会话文件再读回来就没了，卡片会重新变成绿色。
    /// </summary>
    [Fact]
    public void CancelledToolResult_ShowsAsFailed()
    {
        var (transcript, items) = Create();
        transcript.Apply(new FunctionCallContent("a", "ViewImage", null));
        transcript.Apply(new FunctionResultContent("a", ToolCallCancellation.ResultText));

        ToolCallItem call = items.OfType<ToolCallItem>().Single();
        Assert.False(call.IsRunning);
        Assert.False(call.IsSuccess);
    }

    /// <summary>
    /// 外驱会话是按「每次服务调用」补渲染的：工具调用与它的结果落在<b>不同批</b>里。
    /// 只认本批的话结果永远配不上调用，卡片会一直停在「历史里没有这次调用的结果」，
    /// 关掉界面重开(整份回放)才对上——这正是子代理窗口那个 bug
    /// </summary>
    [Fact]
    public void ToolResult_PairsWithACallRenderedInAnEarlierBatch()
    {
        List<ConversationItemBase> rendered = new();
        ConversationTranscript first = new(rendered, () => new TextConversationItem(false));
        first.Apply(new FunctionCallContent("a", "run_shell", null));

        List<ConversationItemBase> batch = new();
        ConversationTranscript second = new(batch, () => new TextConversationItem(false),
            renderedBefore: rendered);
        second.Apply(new FunctionResultContent("a", "ok"));

        ToolCallItem call = rendered.OfType<ToolCallItem>().Single();
        Assert.False(call.IsRunning);
        Assert.True(call.IsSuccess);
        Assert.Equal("ok", call.ResultText);
        Assert.Empty(batch); //结果只回写卡片,不该再造一个条目
    }

    /// <summary>委派入口同样跨批到达：结果先到,SubSessionStartedContent 也要认得回更早那张卡</summary>
    [Fact]
    public void SubSessionStarted_PairsWithACallRenderedInAnEarlierBatch()
    {
        List<ConversationItemBase> rendered = new();
        ConversationTranscript first = new(rendered, () => new TextConversationItem(false));
        first.Apply(new FunctionCallContent("a", "sub_agent", null));

        ConversationTranscript second = new(new List<ConversationItemBase>(),
            () => new TextConversationItem(false), renderedBefore: rendered);
        second.Apply(new SubSessionStartedContent("a", "sub-1"));

        Assert.Equal("sub-1", rendered.OfType<ToolCallItem>().Single().SubSessionId);
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
        transcript.Apply(new FunctionCallContent("call-1", "ViewImage", null));

        //卡在工具调用上停止:没有任何在流的正文,一个字都不该落库
        Assert.Null(transcript.TakeStreamingText());
    }

    [Fact]
    public void StopRunningToolCalls_ClosesUnfinishedCardsAsFailed()
    {
        var (transcript, items) = Create();
        transcript.Apply(new FunctionCallContent("done", "run_shell", null));
        transcript.Apply(new FunctionResultContent("done", "ok"));
        transcript.Apply(new FunctionCallContent("running", "ViewImage", null));

        transcript.StopRunningToolCalls("已停止");

        ToolCallItem finished = items.OfType<ToolCallItem>().Single(x => x.CallId == "done");
        ToolCallItem stopped = items.OfType<ToolCallItem>().Single(x => x.CallId == "running");
        Assert.True(finished.IsSuccess); //已经拿到结果的卡片不该被改判
        Assert.Equal("ok", finished.ResultText);
        Assert.False(stopped.IsRunning); //不收的话卡片会一直转圈,看着像还在跑
        Assert.False(stopped.IsSuccess); //它确实没跑完,绿点会是假消息
        Assert.Equal("已停止", stopped.ResultText);
    }

    /// <summary>
    /// 派活的那一刻子会话标识就该挂到卡片上——「跑着的时候点开看看」正是这件事的重点，
    /// 而工具结果要等跑完才有。
    /// </summary>
    [Fact]
    public void SubSessionStarted_AttachesIdToItsCard()
    {
        var (transcript, items) = Create();
        transcript.Apply(new FunctionCallContent("outer", SubAgentTool.ToolName, null));
        transcript.Apply(new FunctionCallContent("other", "run_shell", null));
        transcript.Apply(new SubSessionStartedContent("outer", "sub123"));

        ToolCallItem delegated = items.OfType<ToolCallItem>().Single(x => x.CallId == "outer");
        ToolCallItem plain = items.OfType<ToolCallItem>().Single(x => x.CallId == "other");
        Assert.Equal("sub123", delegated.SubSessionId);
        Assert.True(delegated.HasSubSession);
        Assert.False(plain.HasSubSession); //普通工具调用不该长出入口
    }

    /// <summary>
    /// 回放历史时 <c>SubSessionStartedContent</c> 早已随当时那一轮消失，
    /// 入口只能从落了盘的工具结果里认回来——否则重开会话后子会话就再也打不开了。
    /// </summary>
    [Fact]
    public void SubSessionId_IsRecoveredFromPersistedResult()
    {
        var (transcript, items) = Create();
        transcript.Apply(new FunctionCallContent("outer", SubAgentTool.ToolName, null));
        transcript.Apply(new FunctionResultContent("outer", "结论是这样。\n[sub-session: abc987]"));

        ToolCallItem card = items.OfType<ToolCallItem>().Single();
        Assert.Equal("abc987", card.SubSessionId);
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
