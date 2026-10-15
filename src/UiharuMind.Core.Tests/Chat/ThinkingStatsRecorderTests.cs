using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
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
