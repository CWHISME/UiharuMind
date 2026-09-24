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
/// 验的是重试语义：用户/助手消息都以自己为锚——历史截到这条为止（它之前的全部保留），
/// 界面条目从这条气泡起删。助手消息重试不再回溯到它前面的提问（否则重试一条靠前的
/// 回复会把整个对话卷进去），而是触发无输入续写：提问留在历史里，让模型接着生成。
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
        public void Rerun(ChatMessage? input)
        {
            LastInput = input;
            RerunCount++;
        }
        public void NotifySessionsChanged() { }
        public void NotifyItemsWired() { }
    }

    private static ChatSession TransientSession() => new() { IsTransient = true };

    /// <summary>
    /// 重试中间的一条助手回复：以它自己为锚，删掉它及之后；它的提问与更早的对话保留，
    /// 无输入续写（Rerun 收到 null）。
    /// </summary>
    [Fact]
    public void RetryingAssistantMessage_TruncatesFromItselfAndRerunsWithoutInput()
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

            replyBubble1.RetryCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            // 历史截到该条为止:它的提问也保留
            Assert.Equal(3, session.History.Count);
            Assert.Same(user0, session.History[0]);
            Assert.Same(reply0, session.History[1]);
            Assert.Same(user1, session.History[2]);

            // 无输入续写,不是拿提问重跑
            Assert.Equal(1, host.RerunCount);
            Assert.Null(host.LastInput);

            // 界面从该条气泡起删:剩 [user0, reply0, user1],不再补用户气泡
            Assert.Equal(3, items.Count);
            Assert.Same(user1, items[2].SourceMessage);
        });
    }

    /// <summary>
    /// 用户报告的场景:重试<b>第一条</b>助手回复。旧实现回溯到它前面的提问重跑整轮,
    /// 而提问就是首句——于是整个对话退回到开头。新语义以它自己为锚,只删它及之后,
    /// 首句提问与后续提问都保留,从这条回复的位置续写。
    /// </summary>
    [Fact]
    public void RetryingFirstAssistantReply_KeepsEverythingBeforeIt()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession session = TransientSession();
            ObservableCollection<ConversationItemBase> items = new();
            RecordingHost host = new(session);
            ConversationItemActions actions = new(items, host);

            ChatMessage opening = new(ChatRole.Assistant, "开场白")
            {
                AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Narration] = true },
            };
            ChatMessage user0 = new(ChatRole.User, "问题1");
            ChatMessage reply0 = new(ChatRole.Assistant, "回答1");
            ChatMessage user1 = new(ChatRole.User, "问题2");
            ChatMessage reply1 = new(ChatRole.Assistant, "回答2");
            session.History.AddRange([opening, user0, reply0, user1, reply1]);

            items.Add(actions.Wire(ConversationItemFactory.CreateNarration(opening), opening));
            items.Add(actions.Wire(ConversationItemFactory.CreateUser("问题1", user0), user0));
            TextConversationItem replyBubble0 = actions.Wire(ConversationItemFactory.CreateAssistant(null), reply0);
            items.Add(replyBubble0);
            items.Add(actions.Wire(ConversationItemFactory.CreateUser("问题2", user1), user1));
            items.Add(actions.Wire(ConversationItemFactory.CreateAssistant(null), reply1));

            replyBubble0.RetryCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            // 开场白 + 首句都保留,删掉的是这条回复及其后
            Assert.Equal(2, session.History.Count);
            Assert.Same(opening, session.History[0]);
            Assert.Same(user0, session.History[1]);
            Assert.Equal(1, host.RerunCount);
            Assert.Null(host.LastInput);

            // 界面:开场白 + 首句气泡保留
            Assert.Equal(2, items.Count);
            Assert.Same(user0, items[1].SourceMessage);
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

            userBubble1.RetryCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            // 用户消息重试语义不变:从它自己起删,以它为输入重跑
            Assert.Equal(2, session.History.Count);
            Assert.Same(user0, session.History[0]);
            Assert.Same(reply0, session.History[1]);
            Assert.Same(user1, host.LastInput);

            Assert.Equal(3, items.Count);
            Assert.Same(user1, items[2].SourceMessage);
        });
    }
}