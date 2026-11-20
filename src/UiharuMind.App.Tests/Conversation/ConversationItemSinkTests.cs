using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 条目集合唯一写入口的判重口径：<b>同一个身份只允许一个条目</b>。
///
/// <para>
/// 从前判重靠对象引用、<c>CallId</c> 与内容启发式三层，每一层都会失配——引用被冷卸载换掉、
/// 思考卡的 <c>Message</c> 是截断预览、知识卡正文在 <c>ResultText</c>。这里钉的是替代它的那条规则：
/// 身份在条目创建时定下，重复与否不再需要猜。
/// </para>
/// </summary>
public class ConversationItemSinkTests
{
    /// <summary>同一条消息的同一个内容块只允许一个条目</summary>
    [Fact]
    public void Add_SameIdentityTwice_KeepsOne()
    {
        ConversationItemSink sink = new(new ObservableCollection<ConversationItemBase>());
        ChatMessage reply = new(ChatRole.Assistant, "答一句。");
        ConversationItemIdentity identity = ConversationItemIdentity.ForMessage(reply, EConversationItemKind.Text);

        TextConversationItem first = new(false, false) { Message = "答一句。" };
        first.SetIdentity(identity);
        TextConversationItem second = new(false, false) { Message = "答一句。" };
        second.SetIdentity(identity);

        Assert.True(sink.Add(first));
        Assert.False(sink.Add(second));
        Assert.Single(sink.Items);
    }

    /// <summary>一条消息拆出的多个条目共享消息标识，靠种类与序号区分，不许互相挤掉</summary>
    [Fact]
    public void Add_OneMessageSeveralKinds_KeepsThemAll()
    {
        ConversationItemSink sink = new(new ObservableCollection<ConversationItemBase>());
        ChatMessage reply = new(ChatRole.Assistant, "答一句。");

        ThinkingItem thinking = new() { Message = "想一下。" };
        thinking.SetIdentity(ConversationItemIdentity.ForMessage(reply, EConversationItemKind.Thinking));
        TextConversationItem bubble = new(false, false) { Message = "答一句。" };
        bubble.SetIdentity(ConversationItemIdentity.ForMessage(reply, EConversationItemKind.Text));
        ToolCallItem card = new() { CallId = "c1", ToolName = "Read" };
        card.SetIdentity(ConversationItemIdentity.ForMessage(reply, EConversationItemKind.ToolCall));

        Assert.Equal(3, sink.AddRange([thinking, bubble, card]));
        Assert.Equal(3, sink.Items.Count);
    }

    /// <summary>同一条消息里两张工具卡靠序号区分（并行调用）</summary>
    [Fact]
    public void Add_TwoToolCardsOfOneMessage_KeepsBoth()
    {
        ConversationItemSink sink = new(new ObservableCollection<ConversationItemBase>());
        ChatMessage reply = new(ChatRole.Assistant, "答一句。");

        ToolCallItem first = new() { CallId = "c1", ToolName = "Read" };
        first.SetIdentity(ConversationItemIdentity.ForMessage(reply, EConversationItemKind.ToolCall, 0));
        ToolCallItem second = new() { CallId = "c2", ToolName = "Read" };
        second.SetIdentity(ConversationItemIdentity.ForMessage(reply, EConversationItemKind.ToolCall, 1));

        Assert.Equal(2, sink.AddRange([first, second]));
    }

    /// <summary>正文相同的两条消息是两条：身份不同，不许按内容合并</summary>
    [Fact]
    public void Add_IdenticalTextDifferentMessages_KeepsBoth()
    {
        ConversationItemSink sink = new(new ObservableCollection<ConversationItemBase>());
        ChatMessage first = new(ChatRole.Assistant, "同一句。");
        ChatMessage second = new(ChatRole.Assistant, "同一句。");

        TextConversationItem a = new(false, false) { Message = "同一句。" };
        a.SetIdentity(ConversationItemIdentity.ForMessage(first, EConversationItemKind.Text));
        TextConversationItem b = new(false, false) { Message = "同一句。" };
        b.SetIdentity(ConversationItemIdentity.ForMessage(second, EConversationItemKind.Text));

        Assert.Equal(2, sink.AddRange([a, b]));
    }

    /// <summary>没有身份的条目（错误提示、占位卡）照常加入，不参与判重</summary>
    [Fact]
    public void Add_WithoutIdentity_AlwaysJoins()
    {
        ConversationItemSink sink = new(new ObservableCollection<ConversationItemBase>());

        Assert.True(sink.Add(new ErrorItem { Message = "出错了" }));
        Assert.True(sink.Add(new ErrorItem { Message = "出错了" }));
        Assert.Equal(2, sink.Items.Count);
    }

    /// <summary>
    /// 临时身份换成正式身份之后，与回放先画的那一条撞上：多画的那份被摘掉。
    /// 这正是从前要靠内容启发式猜的那件事
    /// </summary>
    [Fact]
    public void Rebind_CollidesWithReplayedItem_DropsTheDuplicate()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemSink sink = new(items);
        ChatMessage reply = new(ChatRole.Assistant, "答一句。");

        // 回放先画了（正式身份）
        TextConversationItem replayed = new(false, false) { Message = "答一句。" };
        replayed.SetIdentity(ConversationItemIdentity.ForMessage(reply, EConversationItemKind.Text));
        sink.Add(replayed);

        // 流式那份（临时身份）随后配对到同一条消息
        TextConversationItem streamed = new(false, false) { Message = "答一句。" };
        streamed.SetIdentity(ConversationItemIdentity.Temporary("seg-1", EConversationItemKind.Text));
        sink.Add(streamed);
        Assert.Equal(2, items.Count);

        Assert.False(sink.Rebind(streamed, reply));
        Assert.Single(items);
        Assert.Same(replayed, items[0]);
    }

    /// <summary>配对成功且没有撞车时，条目留在原地、身份变成正式的</summary>
    [Fact]
    public void Rebind_NoCollision_KeepsTheItemAndFormalizesIdentity()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemSink sink = new(items);
        ChatMessage reply = new(ChatRole.Assistant, "答一句。");

        TextConversationItem streamed = new(false, false) { Message = "答一句。" };
        streamed.SetIdentity(ConversationItemIdentity.Temporary("seg-1", EConversationItemKind.Text, 2));
        sink.Add(streamed);

        Assert.True(sink.Rebind(streamed, reply));
        Assert.Single(items);
        Assert.True(streamed.Identity!.Value.IsFormal);
        Assert.Equal(2, streamed.Identity!.Value.ContentOrdinal);
        Assert.True(sink.IsRendered(reply, EConversationItemKind.Text, 2));
    }

    /// <summary>摘掉条目要销掉身份，否则同一条消息再也画不回来</summary>
    [Fact]
    public void Remove_ForgetsTheIdentity()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemSink sink = new(items);
        ChatMessage reply = new(ChatRole.Assistant, "答一句。");

        TextConversationItem item = new(false, false) { Message = "答一句。" };
        item.SetIdentity(ConversationItemIdentity.ForMessage(reply, EConversationItemKind.Text));
        sink.Add(item);

        Assert.True(sink.Remove(item));
        Assert.False(sink.IsRendered(reply, EConversationItemKind.Text));

        TextConversationItem again = new(false, false) { Message = "答一句。" };
        again.SetIdentity(ConversationItemIdentity.ForMessage(reply, EConversationItemKind.Text));
        Assert.True(sink.Add(again));
    }

    /// <summary>整窗重放之后身份索引要跟着重建，否则新画的那批全被判成重复</summary>
    [Fact]
    public void DiscardAll_ClearsTheIndex()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemSink sink = new(items);
        ChatMessage reply = new(ChatRole.Assistant, "答一句。");

        TextConversationItem item = new(false, false) { Message = "答一句。" };
        item.SetIdentity(ConversationItemIdentity.ForMessage(reply, EConversationItemKind.Text));
        sink.Add(item);

        sink.DiscardAll();
        Assert.Empty(items);
        Assert.False(sink.IsRendered(reply, EConversationItemKind.Text));
    }

    /// <summary>裁剪掉一段之后，那一段的身份要销掉（「加载更早」把它们取回来时才画得出来）</summary>
    [Fact]
    public void DiscardRange_ForgetsThoseIdentities()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemSink sink = new(items);
        ChatMessage first = new(ChatRole.User, "第一句");
        ChatMessage second = new(ChatRole.Assistant, "第二句");

        TextConversationItem a = new(true, false) { Message = "第一句" };
        a.SetIdentity(ConversationItemIdentity.ForMessage(first, EConversationItemKind.Text));
        TextConversationItem b = new(false, false) { Message = "第二句" };
        b.SetIdentity(ConversationItemIdentity.ForMessage(second, EConversationItemKind.Text));
        sink.AddRange([a, b]);

        sink.DiscardRange(0, 1);

        Assert.Single(items);
        Assert.False(sink.IsRendered(first, EConversationItemKind.Text));
        Assert.True(sink.IsRendered(second, EConversationItemKind.Text));
    }

    /// <summary>
    /// 删除条目也要销身份：知识卡按内容建身份，删掉后同内容再检索出来的卡必须画得出来
    /// </summary>
    [Fact]
    public void Discard_ForgetsThoseIdentities()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemSink sink = new(items);
        ToolCallItem card = new() { ToolName = "Knowledge" };
        card.SetIdentity(ConversationItemIdentity.ForContent("检索片段", EConversationItemKind.Knowledge));
        sink.Add(card);

        sink.Discard([card]);

        Assert.Empty(items);
        ToolCallItem again = new() { ToolName = "Knowledge" };
        again.SetIdentity(ConversationItemIdentity.ForContent("检索片段", EConversationItemKind.Knowledge));
        Assert.True(sink.Add(again));
    }

    /// <summary>身份不依赖对象引用：同一份文件反序列化出的两个实例指向同一条消息</summary>
    [Fact]
    public void Identity_SurvivesMessageInstanceReplacement()
    {
        const string line = """{"role":"assistant","contents":[{"$type":"text","text":"答一句。"}],"messageId":"fixed-id"}""";
        ChatMessage first = HistoryJsonl.TryParseLine(line)!;
        ChatMessage second = HistoryJsonl.TryParseLine(line)!;
        Assert.NotSame(first, second);

        ConversationItemSink sink = new(new ObservableCollection<ConversationItemBase>());
        TextConversationItem item = new(false, false) { Message = "答一句。" };
        item.SetIdentity(ConversationItemIdentity.ForMessage(first, EConversationItemKind.Text));
        sink.Add(item);

        Assert.True(sink.IsRendered(second, EConversationItemKind.Text));
    }
}
