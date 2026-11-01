using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 子代理来信在界面上的份数。
///
/// 一轮跑着的时候来信被注入并被消费，内容流先画了一份（<c>UserMessageContent</c>）；
/// 随那次服务调用落盘后，历史补画路径不能再画一份。
/// 判据只能是「界面里已经有条目指向这条消息」，不是消息类别——ADR 0062 之后
/// 「报告只在会话空闲时落盘」这个前提不再成立。
/// </summary>
public class HandedBackReportRenderingTests
{
    /// <summary>自己驱动的那一轮（ownTurn）与旁观的那一轮（streaming）都不许出现第二份</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Report_AlreadyDrawnByTheStream_AppearsOnceOnly(bool observedTurn)
    {
        (ObservableCollection<ConversationItemBase> items, ConversationHistoryRenderer renderer) = Fixture();
        ChatMessage report = Report();
        List<ChatMessage> history = [report];

        // 流：注入被消费时画出的那一份，来源就是这条消息本身
        foreach (TextConversationItem item in renderer.CreateUserItems(report)) items.Add(item);
        Assert.Single(items);

        // 落盘：同一条消息进了历史，界面收到追加通知
        renderer.AppendPersisted(history, 0, ownTurn: !observedTurn, streaming: observedTurn);

        Assert.Single(items);
    }

    /// <summary>没有流画过的时候照旧从历史补画：这条通道不能因为去重被削掉</summary>
    [Fact]
    public void Report_NotDrawnYet_IsDrawnFromHistory()
    {
        (ObservableCollection<ConversationItemBase> items, ConversationHistoryRenderer renderer) = Fixture();
        List<ChatMessage> history = [Report()];

        renderer.AppendPersisted(history, 0, ownTurn: true, streaming: false);

        Assert.Single(items);
    }

    private static ChatMessage Report() =>
        new(ChatRole.User, "来自 崖的立面表达重做，回你之前发的「几何这层我认可了」：\n\n【他什么也没回复。】")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.SubAgentReport] = "sub-1",
            },
        };

    private static (ObservableCollection<ConversationItemBase> Items, ConversationHistoryRenderer Renderer) Fixture()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemActions actions = new(items, new StubHost(), new RecordingMessageService());
        ConversationHistoryRenderer renderer = new(items, actions, () => null, () => false, () => null, () => null);
        return (items, renderer);
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
