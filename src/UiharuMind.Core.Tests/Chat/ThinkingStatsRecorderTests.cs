using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
// 项目自有的 struct ChatMessage 遮蔽了 Microsoft.Extensions.AI.ChatMessage，阶段 2 会删除它
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 思考统计兜底盖章的承重假设：无界面轮次（子代理、定时任务）没有视图模型盖章，
/// <see cref="ThinkingStatsRecorder"/> 必须按同一口径独立把耗时写进历史，
/// 否则重开子会话的思考卡片一律回落到纯字数、没有速度。
/// </summary>
public class ThinkingStatsRecorderTests
{
    /// <summary>
    /// 整段重发：界面摘卡并复位，计时器必须同步撤掉那一发的思考段，
    /// 否则两次尝试会被并成一段，字数与耗时都记在一条已落盘的消息上。
    /// </summary>
    [Fact]
    public void StreamRestart_RollsBackTheDiscardedAttempt()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteCallStarted();
        recorder.NoteContent(new TextReasoningContent("第一发的思考"));
        recorder.NoteContent(StreamRestartContent.Instance);
        recorder.NoteContent(new TextReasoningContent("重发的思考"));
        recorder.NoteContent(MessageBoundaryContent.Instance);

        Assert.Equal("重发的思考".Length, recorder.TotalThinkingChars);
    }

    /// <summary>重发前已经被工具调用收过口的段，也要一起退回</summary>
    [Fact]
    public void StreamRestart_DropsSegmentsClosedInsideTheDiscardedAttempt()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteCallStarted();
        recorder.NoteContent(new TextReasoningContent("甲"));
        recorder.NoteContent(new FunctionCallContent("call-1", "Read"));
        recorder.NoteContent(new TextReasoningContent("乙"));
        recorder.NoteContent(StreamRestartContent.Instance);
        recorder.NoteContent(new TextReasoningContent("丙"));
        recorder.NoteContent(MessageBoundaryContent.Instance);

        Assert.Equal("丙".Length, recorder.TotalThinkingChars);
    }

    /// <summary>一轮多次服务调用：回滚水位按调用刷新，重发不能删掉上一发已收尾的段</summary>
    [Fact]
    public void ServiceCallStarted_MovesTheRollbackMarkPerCall()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteCallStarted();
        recorder.NoteContent(ServiceCallStartedContent.Instance);
        recorder.NoteContent(new TextReasoningContent("第一发"));
        recorder.NoteContent(MessageBoundaryContent.Instance);
        recorder.NoteContent(ServiceCallStartedContent.Instance); //第二次调用
        recorder.NoteContent(new TextReasoningContent("作废"));
        recorder.NoteContent(StreamRestartContent.Instance);
        recorder.NoteContent(new TextReasoningContent("重发"));
        recorder.NoteContent(MessageBoundaryContent.Instance);

        Assert.Equal("第一发".Length + "重发".Length, recorder.TotalThinkingChars);
    }

    /// <summary>
    /// 迟到的落盘信号已经盖过章、随后又重发：已盖章的段属于更早的调用，
    /// 回滚不能把盖章游标退回去，否则轮末会把那一段再摊一次。
    /// </summary>
    [Fact]
    public void StreamRestart_AfterLateStamp_KeepsStampedSegmentCursor()
    {
        ThinkingStatsRecorder recorder = new();
        ChatMessage first = new(ChatRole.Assistant, [new TextReasoningContent("第一发的思考")]);
        List<ChatMessage> history = [first];

        recorder.NoteCallStarted();
        recorder.NoteContent(ServiceCallStartedContent.Instance);
        recorder.NoteContent(new TextReasoningContent("第一发的思考"));
        recorder.NoteContent(new TextContent("回答"));
        recorder.NoteContent(MessageBoundaryContent.Instance);
        recorder.Stamp(history, 0);
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(first, out _, out long firstChars));

        recorder.NoteContent(ServiceCallStartedContent.Instance); //第二次调用
        recorder.NoteContent(new TextReasoningContent("作废"));
        recorder.NoteContent(MessageBoundaryContent.Instance); //迟到的落盘，也盖一次
        recorder.Stamp(history, 0);
        recorder.NoteContent(StreamRestartContent.Instance);
        recorder.NoteContent(new TextReasoningContent("重发的思考"));
        history.Add(new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("重发的思考")]));
        recorder.NoteContent(MessageBoundaryContent.Instance);
        recorder.Stamp(history, 0);

        Assert.Equal("第一发的思考".Length + "重发的思考".Length, recorder.TotalThinkingChars);
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(first, out _, out long after));
        Assert.Equal(firstChars, after);
    }

    [Fact]
    public void DistributeDurations_SplitsProportionallyAndSumsToTotal()
    {
        long[] shares = ThinkingStatsRecorder.DistributeDurations(100, [1, 3]);

        Assert.Equal([25, 75], shares);
    }

    [Fact]
    public void DistributeDurations_LargestRemainderKeepsSum()
    {
        long[] shares = ThinkingStatsRecorder.DistributeDurations(100, [1, 1, 1]);

        Assert.Equal(100, shares.Sum());
        Assert.Contains(34, shares);
    }

    [Fact]
    public void DistributeDurations_ZeroInputsYieldZeros()
    {
        Assert.Equal([0, 0], ThinkingStatsRecorder.DistributeDurations(0, [5, 5]));
        Assert.Equal([0, 0], ThinkingStatsRecorder.DistributeDurations(100, [0, 0]));
        Assert.Empty(ThinkingStatsRecorder.DistributeDurations(100, []));
    }

    [Fact]
    public void AttributeSegments_GivesEachMessageItsOwnSegments()
    {
        // 一轮多次工具往返时各次思考快慢差得远，按字数比例摊会把长短对调
        long[]? shares = ThinkingStatsRecorder.AttributeSegments([(30_000, 100), (2_000, 300), (1_000, 600)], [100, 900]);

        Assert.NotNull(shares);
        Assert.Equal([30_000L, 3_000L], shares);
    }

    [Fact]
    public void AttributeSegments_MismatchFallsBackToNull()
    {
        // 取消截掉的半段没进历史，段与消息对不上
        Assert.Null(ThinkingStatsRecorder.AttributeSegments([(1_000, 100), (1_000, 50)], [100]));
        Assert.Null(ThinkingStatsRecorder.AttributeSegments([(1_000, 100)], [60, 40]));
    }

    /// <summary>
    /// 零字思考的耗时不能被按字数分摊吃掉：它只认自己那一段，
    /// 有正文的消息也只认自己那几段——两边的数都不许互相抬高。
    /// </summary>
    [Fact]
    public void Stamp_BodilessAndTextSegments_DoNotStealFromEachOther()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new TextReasoningContent("先想想。"));
        recorder.NoteSegmentClosed();
        recorder.NoteCallStarted();
        Thread.Sleep(5); //零字段的起点是调用发出那一刻，信号要到流尾才来
        recorder.NoteContent(new ThinkingNotReturnedContent(790));
        recorder.NoteSegmentClosed();

        ChatMessage textReply = new(ChatRole.Assistant, [new TextReasoningContent("先想想。")]);
        ChatMessage bodiless = new(ChatRole.Assistant, [new TextContent("答")]);
        ChatMessageAnnotations.WriteThinkingUnavailable(bodiless, 790);
        List<ChatMessage> history = [textReply, bodiless];

        recorder.Stamp(history, 0);

        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(textReply, out TimeSpan textMs, out long chars));
        Assert.Equal("先想想。".Length, chars);
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(bodiless, out TimeSpan bodilessMs, out long bodilessChars));
        Assert.Equal(0, bodilessChars);
        Assert.True(bodilessMs >= TimeSpan.FromMilliseconds(4), $"零字段应拿到自己那 5ms，实际 {bodilessMs.TotalMilliseconds}ms");
        Assert.True(textMs < bodilessMs, $"有正文那段不该被抬高：text={textMs.TotalMilliseconds}ms bodiless={bodilessMs.TotalMilliseconds}ms");
    }

    /// <summary>报了思考 token、一个字没回的调用：消息上盖得到耗时，字数恒为 0。</summary>
    [Fact]
    public void Stamp_BodilessThinkingMessage_GetsDuration()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteCallStarted();
        Thread.Sleep(5); //零字段的起点是调用发出那一刻，信号要到流尾才来：中间必须有真实耗时
        recorder.NoteContent(new ThinkingNotReturnedContent(790));
        recorder.NoteSegmentClosed();
        ChatMessage reply = new(ChatRole.Assistant, [new TextContent("答")]);
        ChatMessageAnnotations.WriteThinkingUnavailable(reply, 790);
        List<ChatMessage> history = [reply];

        int stamped = recorder.Stamp(history, 0);

        Assert.Equal(1, stamped);
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(history[0], out _, out long chars));
        Assert.Equal(0, chars);
    }

    [Fact]
    public void Stamp_ReasoningMessage_GetsStats()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new TextReasoningContent("先想想。"));
        recorder.NoteSegmentClosed();
        List<ChatMessage> history = [new(ChatRole.Assistant, [new TextReasoningContent("先想想。")])];

        int stamped = recorder.Stamp(history, 0);

        Assert.Equal(1, stamped);
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(history[0], out _, out long chars));
        Assert.Equal("先想想。".Length, chars);
    }

    /// <summary>
    /// 纯空白开头的思考：界面不为它建卡（会是一张「1 字」假卡），存档也不能把这几字算进去。
    /// 两边口径不一致，存档字数就会比卡里攒的那段多出几个空白，回放认领时那张卡只好
    /// 放弃耗时与字数（它看着像「认错了消息」，其实只是多了几个空格）。
    /// </summary>
    [Fact]
    public void Stamp_LeadingWhitespaceOnlyDelta_IsNotCounted()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new TextReasoningContent(" "));
        recorder.NoteContent(new TextReasoningContent("先想。"));
        recorder.NoteSegmentClosed();
        // 落盘那份带着开头的空白；界面那张卡攒的是剥掉空白后的那三个字
        List<ChatMessage> history = [new(ChatRole.Assistant, [new TextReasoningContent(" 先想。")])];

        Assert.Equal(1, recorder.Stamp(history, 0));
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(history[0], out _, out long chars));
        Assert.Equal("先想。".Length, chars);
    }

    [Fact]
    public void Stamp_SkipsAlreadyStampedAndOutOfRange()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new TextReasoningContent("想。"));
        recorder.NoteSegmentClosed();
        ChatMessage old = new(ChatRole.Assistant, [new TextReasoningContent("旧的想。")]);
        ChatMessageAnnotations.WriteThinkingStats(old, 2000, 4);
        ChatMessage fresh = new(ChatRole.Assistant, [new TextReasoningContent("新的想。")]);
        List<ChatMessage> history = [old, fresh];

        int stamped = recorder.Stamp(history, 1);

        Assert.Equal(1, stamped);
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(old, out TimeSpan duration, out _));
        Assert.Equal(TimeSpan.FromMilliseconds(2000), duration); //旧值不被覆盖
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(fresh, out _, out _));
    }

    [Fact]
    public void Stamp_ThinkTagText_CountsThinkingOnly()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new TextContent("<think>推理过程</think>正式回答"));
        recorder.NoteSegmentClosed();
        List<ChatMessage> history =
            [new(ChatRole.Assistant, [new TextContent("<think>推理过程</think>正式回答")])];

        int stamped = recorder.Stamp(history, 0);

        Assert.Equal(1, stamped);
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(history[0], out _, out long chars));
        Assert.Equal("推理过程".Length, chars);
    }

    [Fact]
    public void Stamp_UserMessageWithThinkTag_IsNotStamped()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new TextContent("<think>用户原文</think>"));
        recorder.NoteSegmentClosed();
        List<ChatMessage> history = [new(ChatRole.User, "<think>用户原文</think>")];

        Assert.Equal(0, recorder.Stamp(history, 0));
    }

    [Fact]
    public void Stamp_WithoutThinking_StampsNothing()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new TextContent("纯正文"));
        recorder.NoteSegmentClosed();
        List<ChatMessage> history = [new(ChatRole.Assistant, "纯正文")];

        Assert.Equal(0, recorder.Stamp(history, 0));
        Assert.False(ChatMessageAnnotations.TryReadThinkingStats(history[0], out _, out _));
    }

    [Fact]
    public void Stamp_UnclosedSegment_IsFlushedDefensively()
    {
        // 失败路径可能没走 CloseSegment：盖章时自己收尾，不能把本段思考弄丢
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new TextReasoningContent("想。"));
        List<ChatMessage> history = [new(ChatRole.Assistant, [new TextReasoningContent("想。")])];

        Assert.Equal(1, recorder.Stamp(history, 0));
        Assert.Equal(2, recorder.TotalThinkingChars);
    }

    [Fact]
    public void Stamp_ToolCall_StampsDurationOnCallMessage()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new FunctionCallContent("c1", "read_file"));
        recorder.NoteContent(new FunctionResultContent("c1", "正文"));
        List<ChatMessage> history =
            [new(ChatRole.Assistant, [new FunctionCallContent("c1", "read_file")])];

        int stamped = recorder.Stamp(history, 0);

        Assert.Equal(1, stamped);
        Assert.True(ChatMessageAnnotations.TryReadToolDuration(history[0], out TimeSpan duration));
        Assert.True(duration >= TimeSpan.Zero);
    }

    [Fact]
    public void Stamp_ToolCallWithoutResult_StampsNothing()
    {
        // 取消/失败的调用没有结果：没有耗时，不盖（卡片只显示 live 计的 provisional）
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new FunctionCallContent("c1", "read_file"));
        List<ChatMessage> history =
            [new(ChatRole.Assistant, [new FunctionCallContent("c1", "read_file")])];

        Assert.Equal(0, recorder.Stamp(history, 0));
        Assert.False(ChatMessageAnnotations.TryReadToolDuration(history[0], out _));
    }

    [Fact]
    public void Stamp_TwiceConservesTotalAcrossStamps()
    {
        // 轮内多次盖章（每次服务调用落盘后一次、轮末一次）：第二次只分摊新增的段，
        // 已盖的不动，各条之和恒等于计时器总量——否则中途打开的窗口读到的是重摊过的数。
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new TextReasoningContent("第一段想。"));
        Thread.Sleep(20);
        recorder.NoteSegmentClosed();
        List<ChatMessage> history = [new(ChatRole.Assistant, [new TextReasoningContent("第一段想。")])];

        Assert.Equal(1, recorder.Stamp(history, 0));
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(history[0], out TimeSpan first, out _));

        recorder.NoteContent(new TextReasoningContent("第二段想想想。"));
        Thread.Sleep(20);
        recorder.NoteSegmentClosed();
        history.Add(new(ChatRole.Assistant, [new TextReasoningContent("第二段想想想。")]));

        Assert.Equal(1, recorder.Stamp(history, 0));
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(history[0], out TimeSpan firstAgain, out _));
        Assert.Equal(first, firstAgain); //第二次盖章不动已盖的
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(history[1], out TimeSpan second, out _));
        long total = (long)recorder.TotalThinking.TotalMilliseconds;
        Assert.InRange((long)(first + second).TotalMilliseconds, total - 2, total + 2);
    }

    [Fact]
    public void Stamp_ToolsAcrossTwoBoundaries_StampsEach()
    {
        // 工具耗时同理：多次盖章下每次只盖新增收齐的那次，已盖的不碰
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new FunctionCallContent("c1", "read_file"));
        recorder.NoteContent(new FunctionResultContent("c1", "ok"));
        List<ChatMessage> history =
            [new(ChatRole.Assistant, [new FunctionCallContent("c1", "read_file")])];

        Assert.Equal(1, recorder.Stamp(history, 0));

        recorder.NoteContent(new FunctionCallContent("c2", "read_file"));
        recorder.NoteContent(new FunctionResultContent("c2", "ok"));
        history.Add(new(ChatRole.Assistant, [new FunctionCallContent("c2", "read_file")]));

        Assert.Equal(1, recorder.Stamp(history, 0));
        Assert.True(ChatMessageAnnotations.TryReadToolDuration(history[0], out _));
        Assert.True(ChatMessageAnnotations.TryReadToolDuration(history[1], out _));
    }

    [Fact]
    public void Stamp_ToolCall_SkipsAlreadyStamped()
    {
        ThinkingStatsRecorder recorder = new();
        recorder.NoteContent(new FunctionCallContent("c1", "read_file"));
        recorder.NoteContent(new FunctionResultContent("c1", "正文"));
        ChatMessage message = new(ChatRole.Assistant, [new FunctionCallContent("c1", "read_file")]);
        ChatMessageAnnotations.WriteToolDuration(message, 5000);
        List<ChatMessage> history = [message];

        Assert.Equal(0, recorder.Stamp(history, 0));
        Assert.True(ChatMessageAnnotations.TryReadToolDuration(history[0], out TimeSpan duration));
        Assert.Equal(TimeSpan.FromMilliseconds(5000), duration); //旧值不被覆盖
    }
}
