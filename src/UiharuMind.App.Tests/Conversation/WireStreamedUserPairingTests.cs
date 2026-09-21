using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 一轮跑完（或跑到一次落盘）之后，界面上来源还没落到历史里的气泡按角色与历史尾部配对。
///
/// 用户气泡要多问一句「正文对得上吗」：形状对不上时宁可不接，接错了编辑/删除会改错消息。
/// </summary>
public class WireStreamedUserPairingTests
{
    private sealed class StubHost(ChatSession? session) : IConversationItemActionHost
    {
        public ChatSession? Session => session;
        public bool IsGenerating => false;
        public void Rerun(ChatMessage input) { }
        public void NotifySessionsChanged() { }
        public void NotifyItemsWired() { }
    }

    private static (ConversationItemActions Actions, ObservableCollection<ConversationItemBase> Items) Create()
    {
        ObservableCollection<ConversationItemBase> items = new();
        return (new ConversationItemActions(items, new StubHost(null)), items);
    }

    private static TextConversationItem UserBubble(string text) => new(true) { Message = text };

    [Fact]
    public void AnInterjectionBubbleIsNotPairedWithAnEarlierUserMessage()
    {
        var (actions, items) = Create();
        TextConversationItem interjection = UserBubble("555");
        items.Add(interjection);

        // 历史里此刻只有那条任务:插话本身要等下一次服务调用才落盘
        actions.WireStreamed([new ChatMessage(ChatRole.User, "请查看当前工作目录")]);

        Assert.Null(interjection.SourceMessage);
    }

    [Fact]
    public void AUserBubbleIsPairedWhenTheTextMatches()
    {
        var (actions, items) = Create();
        TextConversationItem bubble = UserBubble("555");
        items.Add(bubble);

        ChatMessage landed = new(ChatRole.User, "555");
        actions.WireStreamed([new ChatMessage(ChatRole.User, "请查看当前工作目录"), landed]);

        Assert.Same(landed, bubble.SourceMessage);
    }

    /// <summary>
    /// 实时画出来的用户气泡接的是发送时那个实例，而框架交给持久化的是重建的副本。
    /// 「来源非空」不等于「配好了」——按来源是否在历史里判，副本落盘时把它换过去。
    /// </summary>
    [Fact]
    public void ABubbleWiredToAnUnpersistedInstanceIsRewiredToThePersistedCopy()
    {
        var (actions, items) = Create();
        ChatMessage sent = new(ChatRole.User, "开工");
        TextConversationItem bubble = UserBubble("开工");
        bubble.SourceMessage = sent;
        items.Add(bubble);
        ChatMessage persistedCopy = new(ChatRole.User, "开工");

        actions.WireStreamed([persistedCopy, new ChatMessage(ChatRole.Assistant, "好")]);

        Assert.Same(persistedCopy, bubble.SourceMessage);
    }

    /// <summary>
    /// 助手气泡不做正文比对：它是流式攒出来的，与落盘那份未必逐字相同
    /// （思考段分离、节流冲刷的尾巴），比对只会让它永远配不上。
    /// </summary>
    [Fact]
    public void AssistantBubblesStillPairByRoleAlone()
    {
        var (actions, items) = Create();
        TextConversationItem reply = new(false) { Message = "流式攒出来的正文" };
        items.Add(reply);

        ChatMessage landed = new(ChatRole.Assistant, "落盘的那份正文");
        actions.WireStreamed([landed]);

        Assert.Same(landed, reply.SourceMessage);
    }
}
