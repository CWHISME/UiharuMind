using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 实时与落盘两侧都会画的那几类条目：知识卡、交接文档。
///
/// <para>
/// 它们从前各自靠一条特例判据（「按类别无条件跳过」「只在自己那一轮补画」），
/// 而特例判据只覆盖当时想到的那条路。现在统一问身份：两侧用同一个键，重复的那张自己会被挡掉。
/// </para>
/// </summary>
public class ConversationItemSinkSourceTests
{
    /// <summary>知识卡：实时那张与落盘那张用同一份文本，只留一张</summary>
    [Fact]
    public void KnowledgeCard_RealtimeAndReplay_KeepsOne()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemSink sink = new(items);
        const string snippets = "检索片段 A：材质 Tiling 的换算口径";

        // 实时那张：没有来源消息，按内容建身份
        ToolCallItem live = ConversationItemFactory.CreateKnowledgeCard(snippets);
        live.SetIdentity(ConversationItemIdentity.ForContent(snippets, EConversationItemKind.Knowledge));
        sink.Add(live);

        // 落盘那张：有来源消息，但身份同样按内容建
        ChatMessage persisted = new(ChatRole.Tool, snippets);
        ToolCallItem replayed = ConversationItemFactory.CreateKnowledgeCard(snippets);
        replayed.SourceMessage = persisted;
        replayed.SetIdentity(ConversationItemIdentity.ForContent(snippets, EConversationItemKind.Knowledge));

        Assert.False(sink.Add(replayed));
        Assert.Single(items);
    }

    /// <summary>两轮检索出不同片段是两张卡（内容键不同）</summary>
    [Fact]
    public void KnowledgeCards_WithDifferentSnippets_KeepBoth()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemSink sink = new(items);

        foreach (string snippets in new[] { "片段 A", "片段 B" })
        {
            ToolCallItem card = ConversationItemFactory.CreateKnowledgeCard(snippets);
            card.SetIdentity(ConversationItemIdentity.ForContent(snippets, EConversationItemKind.Knowledge));
            sink.Add(card);
        }

        Assert.Equal(2, items.Count);
    }

    /// <summary>交接文档：落盘补画与通知补画撞上时只留一张</summary>
    [Fact]
    public void HandoffCard_DrawnTwice_KeepsOne()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemSink sink = new(items);
        ChatMessage note = HistoryHandoff.CreateNote("交接正文");

        HandoffItem first = new() { Message = "交接正文", SourceMessage = note };
        first.SetIdentity(ConversationItemIdentity.ForMessage(note, EConversationItemKind.Handoff));
        Assert.True(sink.Add(first));

        HandoffItem second = new() { Message = "交接正文", SourceMessage = note };
        second.SetIdentity(ConversationItemIdentity.ForMessage(note, EConversationItemKind.Handoff));
        Assert.False(sink.Add(second));

        Assert.Single(items);
    }

    /// <summary>
    /// 回放自己负责清空：多一个调用点也不会把上一份留在集合里。
    /// 从前正确性依赖唯一调用点先调 <c>ClearStreamState</c>
    /// </summary>
    [Fact]
    public void Replay_ClearsWhatWasThereBefore()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemSink sink = new(items);
        ConversationItemActions actions = new(items, new StubHost(), new RecordingMessageService(), sink);
        ConversationHistoryRenderer renderer = new(sink, actions, () => null, () => false, () => null, () => null);
        ConversationHistoryPager pager = new(sink, renderer, () => [], () => true, () => true, () => false);

        List<ChatMessage> first = [new(ChatRole.User, "第一份"), new(ChatRole.Assistant, "答一句")];
        pager.Replay(first, liveTail: false);
        Assert.Equal(2, items.Count);

        List<ChatMessage> second = [new(ChatRole.User, "第二份"), new(ChatRole.Assistant, "答两句")];
        pager.Replay(second, liveTail: false);

        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, x => x.Message == "第一份");
    }

    private sealed class StubHost : IConversationItemActionHost
    {
        public ChatSession? Session => null;
        public bool IsGenerating => false;

        public void Rerun(ChatMessage? input)
        {
        }

        public void NotifySessionsChanged()
        {
        }

        public void NotifyItemsWired()
        {
        }
    }
}
