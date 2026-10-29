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

    [Fact]
    public void ToolDuration_WriteRead_RoundTrips()
    {
        ChatMessage message = new(ChatRole.Assistant,
            [new FunctionCallContent("c1", "read_file")]);

        ChatMessageAnnotations.WriteToolDuration(message, 3200);

        Assert.True(ChatMessageAnnotations.TryReadToolDuration(message, out TimeSpan duration));
        Assert.Equal(TimeSpan.FromMilliseconds(3200), duration);
    }

    [Fact]
    public void ToolDuration_JsonRoundTrip_SurvivesAsJsonElement()
    {
        ChatMessage message = new(ChatRole.Assistant,
            [new FunctionCallContent("c1", "read_file")]);
        ChatMessageAnnotations.WriteToolDuration(message, 3200);

        string json = JsonSerializer.Serialize(message, SessionJsonOptions.Default);
        ChatMessage restored = JsonSerializer.Deserialize<ChatMessage>(json, SessionJsonOptions.Default)!;

        Assert.True(ChatMessageAnnotations.TryReadToolDuration(restored, out TimeSpan duration));
        Assert.Equal(TimeSpan.FromMilliseconds(3200), duration);
    }

    [Fact]
    public void ToolDuration_MissingKey_ReturnsFalse()
    {
        ChatMessage message = new(ChatRole.Assistant, "答");

        Assert.False(ChatMessageAnnotations.TryReadToolDuration(message, out _));
    }

    [Fact]
    public void ThinkingUnavailable_WriteRead_RoundTrips()
    {
        ChatMessage message = new(ChatRole.Assistant, "答");

        ChatMessageAnnotations.WriteThinkingUnavailable(message, 790);

        Assert.True(ChatMessageAnnotations.TryReadThinkingUnavailable(message, out long tokens));
        Assert.Equal(790, tokens);
    }

    /// <summary>
    /// 落盘往返后值变成 <c>JsonElement</c>：重开会话时「加密思考」卡还在，读不回来就变成普通思考卡。
    /// </summary>
    [Fact]
    public void ThinkingUnavailable_JsonRoundTrip_SurvivesAsJsonElement()
    {
        ChatMessage message = new(ChatRole.Assistant, "答");
        ChatMessageAnnotations.WriteThinkingUnavailable(message, 790);

        string json = JsonSerializer.Serialize(message, SessionJsonOptions.Default);
        ChatMessage restored = JsonSerializer.Deserialize<ChatMessage>(json, SessionJsonOptions.Default)!;

        Assert.True(ChatMessageAnnotations.TryReadThinkingUnavailable(restored, out long tokens));
        Assert.Equal(790, tokens);
    }

    [Fact]
    public void ThinkingUnavailable_MissingKey_ReturnsFalse()
    {
        ChatMessage message = new(ChatRole.Assistant, "答");

        Assert.False(ChatMessageAnnotations.TryReadThinkingUnavailable(message, out _));
    }

    /// <summary>零思考 token 不算「想过」：这条路径只在服务端确实报了数时才成立。</summary>
    [Fact]
    public void ThinkingUnavailable_ZeroTokens_ReturnsFalse()
    {
        ChatMessage message = new(ChatRole.Assistant, "答");
        ChatMessageAnnotations.WriteThinkingUnavailable(message, 0);

        Assert.False(ChatMessageAnnotations.TryReadThinkingUnavailable(message, out _));
    }
}
