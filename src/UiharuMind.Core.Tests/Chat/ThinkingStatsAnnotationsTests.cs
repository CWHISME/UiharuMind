using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
// 项目自有的 struct ChatMessage 遮蔽了 Microsoft.Extensions.AI.ChatMessage，阶段 2 会删除它
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 思考统计标注的承重假设：落盘往返后值变成 <c>JsonElement</c> 也必须读得回来，
/// 否则重开会话的思考卡片一律回落到纯字数。
/// </summary>
public class ThinkingStatsAnnotationsTests
{
    [Fact]
    public void WriteRead_RoundTrips()
    {
        ChatMessage message = new(ChatRole.Assistant, "答");

        ChatMessageAnnotations.WriteThinkingStats(message, 2500, 1234);

        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(message, out TimeSpan duration, out long chars));
        Assert.Equal(TimeSpan.FromMilliseconds(2500), duration);
        Assert.Equal(1234, chars);
    }

    [Fact]
    public void Read_MissingKeys_ReturnsFalse()
    {
        ChatMessage message = new(ChatRole.Assistant, "答");

        Assert.False(ChatMessageAnnotations.TryReadThinkingStats(message, out _, out _));
    }

    [Fact]
    public void Read_NegativeValues_ReturnsFalse()
    {
        ChatMessage message = new(ChatRole.Assistant, "答");
        ChatMessageAnnotations.WriteThinkingStats(message, -1, 10);

        Assert.False(ChatMessageAnnotations.TryReadThinkingStats(message, out _, out _));
    }

    [Fact]
    public void JsonRoundTrip_SurvivesAsJsonElement()
    {
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("先想。")]);
        ChatMessageAnnotations.WriteThinkingStats(message, 2340, 567);

        string json = JsonSerializer.Serialize(message, SessionJsonOptions.Default);
        ChatMessage restored = JsonSerializer.Deserialize<ChatMessage>(json, SessionJsonOptions.Default)!;

        Assert.True(ChatMessageAnnotations.TryReadThinkingStats(restored, out TimeSpan duration, out long chars));
        Assert.Equal(TimeSpan.FromMilliseconds(2340), duration);
        Assert.Equal(567, chars);
    }
}
