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
    /// <summary>还在流的时候不满一秒不报速度：每拍重算，瞬时值会上下乱跳</summary>
    [Fact]
    public void FormatStats_LiveSubSecond_HasNoSpeedSegment()
    {
        string expected = string.Format(Loc.Text("AgentThinkingStatsFormat"), "0.3s", "12", string.Empty);

        Assert.Equal(expected, ThinkingItem.FormatStats(TimeSpan.FromMilliseconds(300), 12));
    }

    /// <summary>定格之后数不再变：不满一秒也照算</summary>
    [Fact]
    public void FormatStats_FinalSubSecond_HasSpeed()
    {
        string suffix = string.Format(Loc.Text("AgentThinkingSpeedFormat"), ((long)40).ToString("N0"));
        string expected = string.Format(Loc.Text("AgentThinkingStatsFormat"), "0.3s", "12", suffix);

        Assert.Equal(expected, ThinkingItem.FormatStats(TimeSpan.FromMilliseconds(300), 12, isFinal: true));
    }

    /// <summary>短到 0.1s 以下多半是整段一个包到的：分母是包间隔，速度没有意义</summary>
    [Fact]
    public void FormatStats_FinalButTooShort_HasNoSpeedSegment()
    {
        string expected = string.Format(Loc.Text("AgentThinkingStatsFormat"), "0s", "12", string.Empty);

        Assert.Equal(expected, ThinkingItem.FormatStats(TimeSpan.FromMilliseconds(40), 12, isFinal: true));
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
    public void FreezeReplayItems_WithStats_ReadsArchive()
    {
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("先想。")]);
        ChatMessageAnnotations.WriteThinkingStats(message, 2500, 1234);
        ThinkingItem thinking = new();
        thinking.Append("先想。");
        thinking.Flush();

        ThinkingItem.FreezeReplayItems([thinking], message);

        Assert.Equal(ThinkingItem.FormatStats(TimeSpan.FromSeconds(2.5), 1234, isFinal: true), thinking.StatsText);
    }

    [Fact]
    public void FreezeReplayItems_WithoutStats_ShowsCharsOnly()
    {
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("先想。")]);
        ThinkingItem thinking = new();
        thinking.Append("先想后想");
        thinking.Flush();

        ThinkingItem.FreezeReplayItems([thinking], message);

        string expected = string.Format(Loc.Text("AgentThinkingCharsFormat"), thinking.FullLength.ToString("N0"));
        Assert.Equal(expected, thinking.StatsText);
    }

    [Fact]
    public void FreezeReplayItems_SplitsMergedStatsAcrossCards()
    {
        // 存档是整条消息的合并值：拆成两张卡时各挂合并值，就显示成两倍耗时
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("一二三四")]);
        ChatMessageAnnotations.WriteThinkingStats(message, 4000, 4);
        ThinkingItem first = Closed("一");
        ThinkingItem second = Closed("二三四");

        ThinkingItem.FreezeReplayItems([first, second], message);

        Assert.Equal(TimeSpan.FromSeconds(1), first.ClosedElapsed);
        Assert.Equal(TimeSpan.FromSeconds(3), second.ClosedElapsed);
    }

    [Fact]
    public void AdoptPersistedStats_ObservedCardTakesTheDriversArchive()
    {
        // 旁观窗口自己的计时不可信（中途打开时积压内容一口气补发）：轮末改读驱动方落的盘
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("想。")]);
        ThinkingItem observed = Closed("想。", message);
        ChatMessageAnnotations.WriteThinkingStats(message, 7000, 2);

        Assert.Equal(1, ThinkingItem.AdoptPersistedStats([observed]));
        Assert.Equal(TimeSpan.FromSeconds(7), observed.ClosedElapsed);
        // 接着在这个窗口自己发话时，本轮的盖章不能拿旁观计时盖掉存档
        Assert.Equal(0, ThinkingItem.StampLiveItems([observed]));
        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(message, out TimeSpan duration, out _));
        Assert.Equal(TimeSpan.FromSeconds(7), duration);
    }

    [Fact]
    public void AdoptPersistedStats_UpgradesCharsOnlyReplay()
    {
        // 本轮已落盘的消息回放时驱动方还没盖章，只能定格字数；轮末盖上了就该读到
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("想。")]);
        ThinkingItem replayed = Closed("想。", message);
        ThinkingItem.FreezeReplayItems([replayed], message);
        ChatMessageAnnotations.WriteThinkingStats(message, 3000, 2);

        Assert.Equal(1, ThinkingItem.AdoptPersistedStats([replayed]));
        Assert.Equal(ThinkingItem.FormatStats(TimeSpan.FromSeconds(3), 2, isFinal: true), replayed.StatsText);
    }

    [Fact]
    public void AdoptPersistedStats_LeavesOwnStampedCardsAlone()
    {
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("想。")]);
        ThinkingItem own = Closed("想。", message);
        ThinkingItem.StampLiveItems([own]);

        Assert.Equal(0, ThinkingItem.AdoptPersistedStats([own]));
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

    private static ThinkingItem Closed(string text, ChatMessage? source = null)
    {
        ThinkingItem thinking = new() { SourceMessage = source };
        thinking.Append(text);
        thinking.Flush();
        return thinking;
    }
}
