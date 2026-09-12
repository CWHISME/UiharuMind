using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils.Tools;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 思考统计：速度格式、回放冻结、写回历史。速度分母是总耗时、分子是全文长度——
/// 与「统计用全文长度，不随截断缩水」同一口径，否则截断会把速度拉低。
/// </summary>
public class ThinkingItemStatsTests
{
    [Fact]
    public void FormatStats_SubSecond_HasNoSpeedSegment()
    {
        string expected = string.Format(Loc.Text("AgentThinkingStatsFormat"), "0.3s", "12", string.Empty);

        Assert.Equal(expected, ThinkingItem.FormatStats(TimeSpan.FromMilliseconds(300), 12));
    }

    [Fact]
    public void FormatStats_WithSpeed_AppendsIntegerSpeed()
    {
        string suffix = string.Format(Loc.Text("AgentThinkingSpeedFormat"), ((long)617).ToString("N0"));
        string expected = string.Format(Loc.Text("AgentThinkingStatsFormat"), "2s", (1234).ToString("N0"), suffix);

        Assert.Equal(expected, ThinkingItem.FormatStats(TimeSpan.FromSeconds(2), 1234));
    }

    [Fact]
    public void ApplyPersistedStats_SurvivesFlushAndPump()
    {
        ThinkingItem thinking = new();
        thinking.Append("推理内容");
        thinking.Flush();
        thinking.ApplyPersistedStats(TimeSpan.FromSeconds(2.5), 1234);
        string frozen = thinking.StatsText;

        thinking.Flush();
        ((IStreamFlushTarget)thinking).FlushForDisplay();

        Assert.Equal(frozen, thinking.StatsText);
    }

    [Fact]
    public void FreezeReplayItem_WithStats_ReadsArchive()
    {
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("先想。")]);
        ChatMessageAnnotations.WriteThinkingStats(message, 2500, 1234);
        ThinkingItem thinking = new();
        thinking.Append("先想。");
        thinking.Flush();

        ThinkingItem.FreezeReplayItem(thinking, message);

        Assert.Equal(ThinkingItem.FormatStats(TimeSpan.FromSeconds(2.5), 1234), thinking.StatsText);
    }

    [Fact]
    public void FreezeReplayItem_WithoutStats_ShowsCharsOnly()
    {
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("先想。")]);
        ThinkingItem thinking = new();
        thinking.Append("先想后想");
        thinking.Flush();

        ThinkingItem.FreezeReplayItem(thinking, message);

        string expected = string.Format(Loc.Text("AgentThinkingCharsFormat"), thinking.FullLength.ToString("N0"));
        Assert.Equal(expected, thinking.StatsText);
    }

    [Fact]
    public void StampLiveItems_MergesSegmentsIntoOneMessage()
    {
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("想。")]);
        ThinkingItem first = new() { SourceMessage = message };
        first.Append("一二");
        first.Flush();
        ThinkingItem second = new() { SourceMessage = message };
        second.Append("三四五");
        second.Flush();

        int stamped = ThinkingItem.StampLiveItems(new ConversationItemBase[] { first, second });

        Assert.Equal(1, stamped);
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(message, out _, out long chars));
        Assert.Equal(first.ClosedChars + second.ClosedChars, chars);
    }

    [Fact]
    public void StampLiveItems_SkipsMessageWithoutReasoning()
    {
        // 取消打断的思考段没进历史，回落的来源是猜的——盖上去等于记到别人账上
        ChatMessage message = new(ChatRole.Assistant, [new TextContent("正文")]);
        ThinkingItem thinking = new() { SourceMessage = message };
        thinking.Append("想。");
        thinking.Flush();

        Assert.Equal(0, ThinkingItem.StampLiveItems([thinking]));
        Assert.False(ChatMessageAnnotations.TryReadThinkingStats(message, out _, out _));
    }

    [Fact]
    public void StampLiveItems_SkipsUnwiredAndUnclosed()
    {
        ThinkingItem unwired = new();
        unwired.Append("想。");
        unwired.Flush();

        ThinkingItem unclosed = new() { SourceMessage = new ChatMessage(ChatRole.Assistant, "答") };
        unclosed.Append("想。");

        Assert.Equal(0, ThinkingItem.StampLiveItems(new ConversationItemBase[] { unwired, unclosed }));
    }

    [Fact]
    public void StampLiveItems_DoesNotStampTwice()
    {
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("想。")]);
        ThinkingItem thinking = new() { SourceMessage = message };
        thinking.Append("想。");
        thinking.Flush();

        Assert.Equal(1, ThinkingItem.StampLiveItems([thinking]));
        Assert.Equal(0, ThinkingItem.StampLiveItems([thinking]));
    }
}
