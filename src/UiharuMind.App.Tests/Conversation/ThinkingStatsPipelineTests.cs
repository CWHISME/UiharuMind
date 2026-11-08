using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 思考字数与卡片长度的整条管线：内容流 → 计时器落盘的存档 → 转录器画出的卡 → 回放认领。
///
/// 这三者必须同长同源，否则那张卡在「存档字数 ≠ 卡里攒的长度」时会被判成
/// 「认错了消息」而放弃耗时。线上事故就是这么长出来的：直播卡剥掉了段首空白，
/// 存档却按全额计字，于是任何一段以空白开头的思考都拿不到耗时与字数。
///
/// 用例故意走真实管线（归一化 → 计时器 → 转录器 → 认领），不走手工造的等值样本：
/// 上一轮那个「只改了一半」的修复正是被手工样本放过去的。
/// </summary>
public class ThinkingStatsPipelineTests
{
    private static ConversationTranscript NewTranscript(List<ConversationItemBase> items) =>
        new(items, () => new TextConversationItem(false));

    /// <summary>段首空白的单段思考：三段长度必须都是 3（不是 4），并且读得到存档耗时</summary>
    [Fact]
    public void LeadingWhitespaceSegment_StillReadsTheArchiveDuration()
    {
        // 未归一化的真实形态：纯空白开头 + 相邻碎片 + 空正文
        ChatMessage message = new(ChatRole.Assistant,
        [
            new TextReasoningContent(" "),
            new TextReasoningContent("先想。"),
            new TextContent(""),
        ]);
        ChatContentNormalizer.Normalize(message); // 丢空正文、相邻思考合并成一段

        ThinkingStatsRecorder recorder = new();
        foreach (AIContent content in message.Contents) recorder.NoteContent(content);
        Thread.Sleep(5); //耗时按毫秒记录，同一拍收段会落成 0
        recorder.NoteSegmentClosed();
        Assert.Equal(1, recorder.Stamp([message], 0));

        List<ConversationItemBase> items = new();
        ConversationTranscript transcript = NewTranscript(items);
        foreach (AIContent content in message.Contents) transcript.Apply(content);
        transcript.CloseSegment();

        ThinkingItem card = Assert.Single(items.OfType<ThinkingItem>());
        Assert.Equal(ThinkingSegments.LengthsOf(message), new[] { card.FullLength });
        Assert.Equal(3, card.FullLength);

        ThinkingItem.FreezeReplayItems([card], message);

        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(message, out TimeSpan duration, out long chars));
        Assert.Equal(3, chars);
        // 认领成功而不是被降级成「只有字数」：存档的耗时挂上来了
        Assert.True(card.ClosedElapsed > TimeSpan.Zero);
        Assert.Equal(ThinkingItem.FormatStats(duration, 3, isFinal: true), card.StatsText);
    }

    /// <summary>
    /// 交错消息（思考 → 正文 → 思考）：两段各一张卡，长度与存档的段一一对应，
    /// 冻结后各按字数比例分到耗时——多卡分摊不能被口径改动带坏。
    /// </summary>
    [Fact]
    public void InterleavedSegmentsEachGetTheirOwnCardAndShareTheDuration()
    {
        ChatMessage message = new(ChatRole.Assistant,
        [
            new TextReasoningContent(" 先想。"),
            new TextContent("正文"),
            new TextReasoningContent(" 后想？"),
        ]);
        ChatContentNormalizer.Normalize(message);

        ThinkingStatsRecorder recorder = new();
        // 逐段喂并让两段各自拉开几毫秒：耗时按毫秒记录，同一拍收段会落成 0
        recorder.NoteContent(new TextReasoningContent(" 先想。"));
        Thread.Sleep(5);
        recorder.NoteContent(new TextContent("正文")); // 正文把第一段收了口
        recorder.NoteContent(new TextReasoningContent(" 后想？"));
        Thread.Sleep(5);
        recorder.NoteSegmentClosed();
        recorder.Stamp([message], 0);

        List<ConversationItemBase> items = new();
        ConversationTranscript transcript = NewTranscript(items);
        foreach (AIContent content in message.Contents) transcript.Apply(content);
        transcript.CloseSegment();

        List<ThinkingItem> cards = items.OfType<ThinkingItem>().ToList();
        Assert.Equal(2, cards.Count);
        Assert.Equal(ThinkingSegments.LengthsOf(message), cards.Select(x => x.FullLength).ToList());
        Assert.Equal([3, 3], cards.Select(x => x.FullLength).ToList());

        ThinkingItem.FreezeReplayItems(cards, message);

        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(message, out TimeSpan duration, out long chars));
        Assert.Equal(6, chars);
        Assert.All(cards, card => Assert.True(card.ClosedElapsed > TimeSpan.Zero, "每张卡都该分到一段耗时"));
        TimeSpan total = cards.Aggregate(TimeSpan.Zero, (sum, card) => sum + card.ClosedElapsed);
        Assert.Equal(duration.TotalMilliseconds, total.TotalMilliseconds, 1);
    }
}
