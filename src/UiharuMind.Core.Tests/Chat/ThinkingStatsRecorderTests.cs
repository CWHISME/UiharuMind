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
}
