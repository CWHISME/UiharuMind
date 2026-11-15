using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 补画去重闸的判据：按来源消息引用认人、工具卡按 <c>CallId</c> 认，
/// 只有<b>来源为空</b>的流式条目才按同类同正文认领。正文相同的两条消息仍是两条，不许按内容合并。
/// </summary>
public class ConversationItemDedupTests
{
    /// <summary>流画出但没配对上的正文气泡：补画时认领它，不添第二份</summary>
    [Fact]
    public void Append_UnwiredTextTwin_IsClaimed()
    {
        ObservableCollection<ConversationItemBase> items = new()
        {
            new TextConversationItem(false, false) { Message = "2D 58558 字节确认。" },
        };
        ChatMessage reply = new(ChatRole.Assistant, "2D 58558 字节确认。");

        int added = ConversationItemDedup.Append(items,
            [new TextConversationItem(false, false) { Message = "2D 58558 字节确认。", SourceMessage = reply }]);

        Assert.Equal(0, added);
        Assert.Single(items);
    }

    /// <summary>思考卡同样按内容认领，不然补画会把思考与正文各复制一份</summary>
    [Fact]
    public void Append_UnwiredThinkingTwin_IsClaimed()
    {
        ObservableCollection<ConversationItemBase> items = new()
        {
            new ThinkingItem { Message = "想一下。" },
        };
        ChatMessage reply = new(ChatRole.Assistant, "答一句。");

        int added = ConversationItemDedup.Append(items,
            [new ThinkingItem { Message = "想一下。", SourceMessage = reply }]);

        Assert.Equal(0, added);
        Assert.Single(items);
    }

    /// <summary>工具卡按 CallId 认，来源为空也照样认得出</summary>
    [Fact]
    public void Append_ToolCallByCallId_IsSkipped()
    {
        ObservableCollection<ConversationItemBase> items = new()
        {
            new ToolCallItem { CallId = "c1", ToolName = "Read" },
        };
        ChatMessage reply = new(ChatRole.Assistant, "答一句。");

        int added = ConversationItemDedup.Append(items,
            [new ToolCallItem { CallId = "c1", ToolName = "Read", SourceMessage = reply }]);

        Assert.Equal(0, added);
        Assert.Single(items);
    }

    /// <summary>接连三条一字不差的回复：已配对的不参与内容认领，新那条必须照补</summary>
    [Fact]
    public void Append_ThirdMessageWithSameText_IsNotSwallowed()
    {
        ObservableCollection<ConversationItemBase> items = new()
        {
            new TextConversationItem(false, false) { Message = "同一句。", SourceMessage = new(ChatRole.Assistant, "同一句。") },
            new TextConversationItem(false, false) { Message = "同一句。", SourceMessage = new(ChatRole.Assistant, "同一句。") },
        };
        ChatMessage third = new(ChatRole.Assistant, "同一句。");

        int added = ConversationItemDedup.Append(items,
            [new TextConversationItem(false, false) { Message = "同一句。", SourceMessage = third }]);

        Assert.Equal(1, added);
        Assert.Equal(3, items.Count);
    }

    /// <summary>一条消息拆出的多个条目共享来源，补画时不能被自己挤掉</summary>
    [Fact]
    public void Append_OneMessageSeveralItems_KeepsThemAll()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ChatMessage reply = new(ChatRole.Assistant, "答一句。");

        int added = ConversationItemDedup.Append(items,
        [
            new ThinkingItem { Message = "想一下。", SourceMessage = reply },
            new TextConversationItem(false, false) { Message = "答一句。", SourceMessage = reply },
            new ToolCallItem { CallId = "c1", ToolName = "Read", SourceMessage = reply },
        ]);

        Assert.Equal(3, added);
        Assert.Equal(3, items.Count);
    }

    /// <summary>
    /// 长思考的 Message 是「前 512 字 + 已截断 N 字」的预览，N 随流式长度变化：
    /// 流那张还停在旧预览时与回放那张逐字不等，按全文比对才认得出是同一段。
    /// </summary>
    [Fact]
    public void Append_LongThinkingCard_IsMatchedByFullText()
    {
        string full = new('想', 1200);
        ThinkingItem streamed = new();
        streamed.Append(full[..1000]);
        streamed.Flush();
        streamed.Append(full[1000..]);

        ThinkingItem replayed = new();
        replayed.Append(full);
        replayed.Flush();

        Assert.NotEqual(streamed.Message, replayed.Message);
        Assert.Equal(streamed.CurrentText, replayed.CurrentText);

        ObservableCollection<ConversationItemBase> items = new() { streamed };
        replayed.SourceMessage = new(ChatRole.Assistant, "答一句。");

        int added = ConversationItemDedup.Append(items, [replayed]);

        Assert.Equal(0, added);
        Assert.Single(items);
    }

    /// <summary>知识卡没有来源也没有 CallId，正文在 ResultText：按它认领实时那张</summary>
    [Fact]
    public void Append_KnowledgeCardByResultText_IsClaimed()
    {
        ObservableCollection<ConversationItemBase> items = new()
        {
            ConversationItemFactory.CreateKnowledgeCard("检索片段 A"),
        };
        ToolCallItem replayed = ConversationItemFactory.CreateKnowledgeCard("检索片段 A");
        replayed.SourceMessage = new(ChatRole.Tool, "检索片段 A");

        int added = ConversationItemDedup.Append(items, [replayed]);

        Assert.Equal(0, added);
        Assert.Single(items);
    }
}
