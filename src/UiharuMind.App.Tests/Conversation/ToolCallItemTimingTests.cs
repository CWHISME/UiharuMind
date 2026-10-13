using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 工具卡耗时：只显示不计时。live 给 provisional，来源配好（或回放）后改读存档；
/// 存档缺席（老会话）保持空白，不拿假数充数。
/// </summary>
public class ToolCallItemTimingTests
{
    [Fact]
    public void FreshCard_HasNoDurationText()
    {
        var item = new ToolCallItem { CallId = "c1", ToolName = "read_file" };

        Assert.Equal(string.Empty, item.DurationText);
    }

    [Fact]
    public void FinishProvisionalDuration_FillsDisplayText()
    {
        var item = new ToolCallItem { CallId = "c1", ToolName = "read_file" };
        item.FinishProvisionalDuration();

        Assert.NotEqual(string.Empty, item.DurationText);
    }

    [Fact]
    public void FreezeReplayItems_WithStats_ReadsArchive()
    {
        ChatMessage message = new(ChatRole.Assistant, [new FunctionCallContent("c1", "read_file")]);
        ChatMessageAnnotations.WriteToolDuration(message, 3200);
        var item = new ToolCallItem { CallId = "c1", ToolName = "read_file" };

        ToolCallItem.FreezeReplayItems([item], message);

        Assert.Equal(ToolCallItem.FormatDuration(TimeSpan.FromMilliseconds(3200)), item.DurationText);
    }

    [Theory]
    [InlineData(3200, "3.2s")]
    [InlineData(1000, "1s")]
    [InlineData(85, "85ms")]
    [InlineData(40, "40ms")]
    public void FormatDuration_SubSecond_ShowsMilliseconds(long ms, string expected)
    {
        Assert.Equal(expected, ToolCallItem.FormatDuration(TimeSpan.FromMilliseconds(ms)));
    }

    [Fact]
    public void FormatDuration_Zero_ShowsLessThanOneMillisecond()
    {
        // 真零只可能来自亚毫秒截断：诚实写 <1ms，不写 0s
        Assert.Equal("<1ms", ToolCallItem.FormatDuration(TimeSpan.Zero));
    }

    [Fact]
    public void FreezeReplayItems_WithoutStats_StaysBlank()
    {
        // 老会话没有这个键：空白好过假数
        ChatMessage message = new(ChatRole.Assistant, [new FunctionCallContent("c1", "read_file")]);
        var item = new ToolCallItem { CallId = "c1", ToolName = "read_file" };

        ToolCallItem.FreezeReplayItems([item], message);

        Assert.Equal(string.Empty, item.DurationText);
    }

    [Fact]
    public void AdoptPersistedDurations_ReplacesProvisionalWithArchive()
    {
        ChatMessage message = new(ChatRole.Assistant, [new FunctionCallContent("c1", "read_file")]);
        var item = new ToolCallItem { CallId = "c1", ToolName = "read_file", SourceMessage = message };
        item.FinishProvisionalDuration();
        ChatMessageAnnotations.WriteToolDuration(message, 9500);

        Assert.Equal(1, ToolCallItem.AdoptPersistedDurations([item]));
        Assert.Equal(ToolCallItem.FormatDuration(TimeSpan.FromMilliseconds(9500)), item.DurationText);
        // 认领过的不再重复
        Assert.Equal(0, ToolCallItem.AdoptPersistedDurations([item]));
    }

    [Fact]
    public void AdoptPersistedDurations_SkipsCardsWithoutSourceOrStats()
    {
        var unwired = new ToolCallItem { CallId = "c1", ToolName = "read_file" };
        unwired.FinishProvisionalDuration();
        var unstamped = new ToolCallItem
        {
            CallId = "c2",
            ToolName = "read_file",
            SourceMessage = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c2", "read_file")]),
        };

        Assert.Equal(0, ToolCallItem.AdoptPersistedDurations(new ConversationItemBase[] { unwired, unstamped }));
        // 没存档的可保留 provisional（取消打断等情形）
        Assert.NotEqual(string.Empty, unwired.DurationText);
        Assert.Equal(string.Empty, unstamped.DurationText);
    }
}
