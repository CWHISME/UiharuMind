using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 重试完整流程。挂在无头集合是因为 <see cref="ConversationItemFactory.CreateUser"/>
/// 会取角色头像（Avalonia 资源），普通 xunit 环境没有应用上下文。
///
/// 验的是助手消息重试的语义：锚点回落到它前面最近的用户提问，历史截到锚点为止，
/// 界面条目从锚点气泡起删、尾部重新加回提问气泡，重跑输入正是那条提问。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ConversationItemActionsRetryHeadlessTests
{
    private sealed class RecordingHost(ChatSession session) : IConversationItemActionHost
    {
        public ChatSession? Session => session;
        public bool IsGenerating => false;
        public ChatMessage? LastInput { get; private set; }
        public int RerunCount { get; private set; }
        public void Rerun(ChatMessage input)
        {
            LastInput = input;
            RerunCount++;
        }
        public void NotifySessionsChanged() { }
        public void NotifyItemsWired() { }
    }

    private static ChatSession TransientSession() => new() { IsTransient = true };

    [Fact]
    public void RetryingAssistantMessage_TruncatesHistoryAtAnchorAndRerunsAnchor()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession session = TransientSession();
            ObservableCollection<ConversationItemBase> items = new();
            RecordingHost host = new(session);
            ConversationItemActions actions = new(items, host);

            ChatMessage user0 = new(ChatRole.User, "问题1");
            ChatMessage reply0 = new(ChatRole.Assistant, "回答1");
            ChatMessage user1 = new(ChatRole.User, "问题2");
            ChatMessage reply1 = new(ChatRole.Assistant, "回答2");
            session.History.AddRange([user0, reply0, user1, reply1]);

            items.Add(actions.Wire(ConversationItemFactory.CreateUser("问题1", user0), user0));
            items.Add(actions.Wire(ConversationItemFactory.CreateAssistant(null), reply0));
            items.Add(actions.Wire(ConversationItemFactory.CreateUser("问题2", user1), user1));
            TextConversationItem replyBubble1 = actions.Wire(ConversationItemFactory.CreateAssistant(null), reply1);
            items.Add(replyBubble1);

            replyBubble1.RetryCommand.Execute(null);

            // 历史截到锚点用户消息为止（锚点消息本身由 RunAsync 写回，测试 host 是 stub）
            Assert.Equal(2, session.History.Count);
            Assert.Same(user0, session.History[0]);
            Assert.Same(reply0, session.History[1]);

            // 重跑的是锚点用户消息
            Assert.Equal(1, host.RerunCount);
            Assert.Same(user1, host.LastInput);

            // 界面从锚点气泡起删:剩 [user0, reply0] + 重新加回的用户气泡
            Assert.Equal(3, items.Count);
            Assert.True(items[2].IsUser);
            Assert.Same(user1, items[2].SourceMessage);
        });
    }

    [Fact]
    public void RetryingUserMessage_StillWorksAfterAssistantRetrySupport()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession session = TransientSession();
            ObservableCollection<ConversationItemBase> items = new();
            RecordingHost host = new(session);
            ConversationItemActions actions = new(items, host);

            ChatMessage user0 = new(ChatRole.User, "问题1");
            ChatMessage reply0 = new(ChatRole.Assistant, "回答1");
            ChatMessage user1 = new(ChatRole.User, "问题2");
            ChatMessage reply1 = new(ChatRole.Assistant, "回答2");
            session.History.AddRange([user0, reply0, user1, reply1]);

            TextConversationItem userBubble0 = actions.Wire(ConversationItemFactory.CreateUser("问题1", user0), user0);
            items.Add(userBubble0);
            items.Add(actions.Wire(ConversationItemFactory.CreateAssistant(null), reply0));
            TextConversationItem userBubble1 = actions.Wire(ConversationItemFactory.CreateUser("问题2", user1), user1);
            items.Add(userBubble1);
            items.Add(actions.Wire(ConversationItemFactory.CreateAssistant(null), reply1));

            userBubble1.RetryCommand.Execute(null);

            // 用户消息重试语义不变:从它自己起删
            Assert.Equal(2, session.History.Count);
            Assert.Same(user0, session.History[0]);
            Assert.Same(reply0, session.History[1]);
            Assert.Same(user1, host.LastInput);

            Assert.Equal(3, items.Count);
            Assert.Same(user1, items[2].SourceMessage);
        });
    }
}
