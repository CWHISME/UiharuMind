using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils.Tools;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 思考统计：速度格式、回放冻结、认领存档。速度分母是总耗时、分子是全文长度——
/// 与「统计用全文长度，不随截断缩水」同一口径，否则截断会把速度拉低。
/// 计时权威在 Core（轮末盖进历史），这里只测显示与认领，不测写回。
/// </summary>
public class ThinkingItemStatsTests
{
    /// <summary>还在流也不设限：有字数、有时间就报速度</summary>
    [Fact]
    public void FormatStats_LiveSubSecond_HasSpeed()
    {
        string suffix = string.Format(Loc.Text("AgentThinkingSpeedFormat"), ((long)40).ToString("N0"));
        string expected = string.Format(Loc.Text("AgentThinkingStatsFormat"), "0.3s", "12", suffix);

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

    /// <summary>再短也照算：分母来自 Core 的单调时钟，诚实呈现，耗时并排可判断</summary>
    [Fact]
    public void FormatStats_FinalButVeryShort_HasSpeed()
    {
        string suffix = string.Format(Loc.Text("AgentThinkingSpeedFormat"), ((long)300).ToString("N0"));
        string expected = string.Format(Loc.Text("AgentThinkingStatsFormat"), "0s", "12", suffix);

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
    public void FormatStats_LiveOverOneSecond_FloorsDuration()
    {
        // 节拍晚到几十毫秒是常态：取整后 1s 一跳就是 1s、2s，不会满屏 1.1s、2.1s；
        // 速度用同一个取整值算，190字 / 1s = 190字/秒，读者能对上
        string suffix = string.Format(Loc.Text("AgentThinkingSpeedFormat"), ((long)190).ToString("N0"));
        string expected = string.Format(Loc.Text("AgentThinkingStatsFormat"), "1s", "190", suffix);

        Assert.Equal(expected, ThinkingItem.FormatStats(TimeSpan.FromMilliseconds(1900), 190));
    }

    [Fact]
    public void FormatStats_FinalOverOneSecond_KeepsPrecision()
    {
        // 定格（收尾、存档）保持精确，不取整
        string suffix = string.Format(Loc.Text("AgentThinkingSpeedFormat"), ((long)100).ToString("N0"));
        string expected = string.Format(Loc.Text("AgentThinkingStatsFormat"), "1.9s", "190", suffix);

        Assert.Equal(expected, ThinkingItem.FormatStats(TimeSpan.FromMilliseconds(1900), 190, isFinal: true));
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
        // 认领过的卡不动：再调一次返回 0，存档也不被改写
        Assert.Equal(0, ThinkingItem.AdoptPersistedStats([observed]));
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
    public void AdoptPersistedStats_LeavesAdoptedCardsAlone()
    {
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("想。")]);
        ThinkingItem own = Closed("想。", message);
        ChatMessageAnnotations.WriteThinkingStats(message, 9000, 2);

        Assert.Equal(1, ThinkingItem.AdoptPersistedStats([own]));
        Assert.Equal(0, ThinkingItem.AdoptPersistedStats([own]));
    }

    private static ThinkingItem Closed(string text, ChatMessage? source = null)
    {
        ThinkingItem thinking = new() { SourceMessage = source };
        thinking.Append(text);
        thinking.Flush();
        return thinking;
    }
}
