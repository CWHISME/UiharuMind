using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Shared.Services;
using UiharuMind.App.Tests.TestDoubles;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 重试入口的挂载规则与确认弹窗。
///
/// 重试语义是「从这条消息起重新生成」：用户消息以自己为锚（删掉它及之后，重跑一轮）；
/// 助手消息同样以自己为锚（删掉它及之后，由无输入轮续写新回复）——<b>不再回溯到它前面
/// 最近的提问重跑整轮</b>，否则重试一条靠前的回复会把提问和更早的对话一起卷进去。
/// 旁白（开场白/子代理报告）不提供重试。用户与助手消息重试都先弹确认——删了回不来。
///
/// 完整流程（历史截断 + 界面条目处理）会构造真实气泡、依赖 Avalonia 资源，
/// 放在 <c>Headless</c> 的 <c>ConversationItemActionsRetryHeadlessTests</c>。
/// </summary>
public class ConversationItemActionsRetryTests
{
    private sealed class ProbeItem : ConversationItemBase
    {
    }

    private sealed class RecordingHost(ChatSession session) : IConversationItemActionHost
    {
        public ChatSession? Session => session;
        public bool IsGenerating => false;
        public int RerunCount { get; private set; }
        public ChatMessage? LastInput { get; private set; }
        public void Rerun(ChatMessage? input)
        {
            LastInput = input;
            RerunCount++;
        }
        public void NotifySessionsChanged() { }
        public void NotifyItemsWired() { }
    }

    private static ChatSession TransientSession() => new() { IsTransient = true };

    private static ChatMessage UserMessage(string text) => new(ChatRole.User, text);
    private static ChatMessage AssistantText(string text) => new(ChatRole.Assistant, text);

    [Fact]
    public void WiringAssistantMessage_ProvidesRetry()
    {
        ChatSession session = TransientSession();
        ConversationItemActions actions = new(new ObservableCollection<ConversationItemBase>(), new RecordingHost(session), new RecordingMessageService());

        ChatMessage reply = AssistantText("回答");
        ProbeItem item = actions.Wire(new ProbeItem(), reply);

        Assert.True(item.CanRetry);
    }

    [Fact]
    public void WiringUserMessage_StillProvidesRetry()
    {
        ChatSession session = TransientSession();
        ConversationItemActions actions = new(new ObservableCollection<ConversationItemBase>(), new RecordingHost(session), new RecordingMessageService());

        ChatMessage question = UserMessage("提问");
        ProbeItem item = actions.Wire(new ProbeItem(), question);

        Assert.True(item.CanRetry);
    }

    [Fact]
    public void WiringNarration_DoesNotProvideRetry()
    {
        ChatSession session = TransientSession();
        ConversationItemActions actions = new(new ObservableCollection<ConversationItemBase>(), new RecordingHost(session), new RecordingMessageService());

        ChatMessage narration = new(ChatRole.Assistant, "开场白")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Narration] = true },
        };
        ProbeItem item = actions.Wire(new ProbeItem(), narration);

        Assert.False(item.CanRetry);
    }

    /// <summary>
    /// 助手消息重试不再要求前面存在用户提问：以这条回复自己为锚，删掉它及之后，
    /// 触发无输入续写。历史里只有它一条时，删除后历史为空，Rerun 收到 null。
    /// </summary>
    [Fact]
    public async Task RetryingAssistantWithoutAnchorUser_TruncatesFromItself()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        RecordingHost host = new(session);
        ConversationItemActions actions = new(items, host, new RecordingMessageService());

        ChatMessage reply = AssistantText("开场回复");
        session.History.Add(reply);
        ProbeItem item = actions.Wire(new ProbeItem(), reply);
        items.Add(item);

        await item.RetryCommand.ExecuteAsync(null);

        Assert.Empty(session.History);
        Assert.Empty(items); //条目从自己起删,不留
        Assert.Equal(1, host.RerunCount);
        Assert.Null(host.LastInput); //无输入续写
    }

    /// <summary>
    /// 开场白旁白留在历史里：助手消息重试以自己为锚，旁白不是重试对象，不动它。
    /// </summary>
    [Fact]
    public async Task RetryingAssistantWithNarrationBefore_KeepsNarration()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        RecordingHost host = new(session);
        ConversationItemActions actions = new(items, host, new RecordingMessageService());

        ChatMessage narration = new(ChatRole.User, "开场白")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Narration] = true },
        };
        ChatMessage reply = AssistantText("开场回复");
        session.History.AddRange([narration, reply]);
        ProbeItem item = actions.Wire(new ProbeItem(), reply);
        items.Add(item);

        await item.RetryCommand.ExecuteAsync(null);

        Assert.Single(session.History);
        Assert.Same(narration, session.History[0]);
        Assert.Equal(1, host.RerunCount);
        Assert.Null(host.LastInput);
    }

    /// <summary>
    /// 重试要删掉这条及之后的内容，删了回不来：用户消息与助手消息<b>都先弹确认</b>。
    /// 用户消息这边尤其要问——被删掉的那一轮里往往还连着工具往返与后续对话。
    /// 确认拒绝则历史与条目都不动、也不重跑。
    /// </summary>
    [Fact]
    public async Task RetryingUserMessage_ConfirmsFirst_AndRespectsCancel()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        RecordingHost host = new(session);
        RecordingMessageService confirms = new() { ConfirmResult = false }; //点「取消」
        ConversationItemActions actions = new(items, host, confirms);

        ChatMessage user0 = UserMessage("问题1");
        ChatMessage reply0 = AssistantText("回答1");
        ChatMessage user1 = UserMessage("问题2");
        ChatMessage reply1 = AssistantText("回答2");
        session.History.AddRange([user0, reply0, user1, reply1]);

        ProbeItem item = actions.Wire(new ProbeItem(), user1);
        items.Add(item);

        await item.RetryCommand.ExecuteAsync(null);

        Assert.Equal(1, confirms.ConfirmCount);
        Assert.Contains("2", confirms.LastConfirm); //这一条及之后共 2 条
        Assert.Equal(4, session.History.Count);
        Assert.Single(items);
        Assert.Equal(0, host.RerunCount);
    }

    /// <summary>用户消息确认通过后照旧:从它自己起删,以它为输入重跑</summary>
    [Fact]
    public async Task RetryingUserMessage_ConfirmApproved_RerunsWithThatMessage()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        RecordingHost host = new(session);
        RecordingMessageService confirms = new();
        ConversationItemActions actions = new(items, host, confirms);

        ChatMessage user0 = UserMessage("问题1");
        ChatMessage reply0 = AssistantText("回答1");
        ChatMessage user1 = UserMessage("问题2");
        ChatMessage reply1 = AssistantText("回答2");
        session.History.AddRange([user0, reply0, user1, reply1]);

        ProbeItem item = actions.Wire(new ProbeItem(), user1);
        items.Add(item);

        await item.RetryCommand.ExecuteAsync(null);

        Assert.Equal(1, confirms.ConfirmCount);
        Assert.Equal(2, session.History.Count);
        Assert.Same(user1, host.LastInput);
    }

    /// <summary>
    /// 助手消息同样一律先问：只删它自己一条也问，删了回不来
    /// </summary>
    [Fact]
    public async Task RetryingAssistant_AlwaysConfirms_EvenForASingleMessage()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        RecordingHost host = new(session);
        RecordingMessageService confirms = new() { ConfirmResult = false };
        ConversationItemActions actions = new(items, host, confirms);

        ChatMessage user0 = UserMessage("问题1");
        ChatMessage reply0 = AssistantText("回答1");
        ChatMessage user1 = UserMessage("问题2");
        ChatMessage reply1 = AssistantText("回答2");
        session.History.AddRange([user0, reply0, user1, reply1]);

        ProbeItem item = actions.Wire(new ProbeItem(), reply1);
        items.Add(item);

        await item.RetryCommand.ExecuteAsync(null);

        Assert.Equal(1, confirms.ConfirmCount);
        Assert.Contains("1", confirms.LastConfirm); //只删 1 条也问
        Assert.Equal(4, session.History.Count);
        Assert.Equal(0, host.RerunCount);
    }

    /// <summary>
    /// 同样的大跨度,确认通过则照常执行:从这条起删,无输入续写。
    /// </summary>
    [Fact]
    public async Task RetryingAssistant_ConfirmApproved_TruncatesFromItself()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        RecordingHost host = new(session);
        RecordingMessageService confirms = new() { ConfirmResult = true };
        ConversationItemActions actions = new(items, host, confirms);

        ChatMessage user0 = UserMessage("问题1");
        ChatMessage reply0 = AssistantText("回答1");
        ChatMessage user1 = UserMessage("问题2");
        ChatMessage reply1 = AssistantText("回答2");
        ChatMessage user2 = UserMessage("问题3");
        ChatMessage reply2 = AssistantText("回答3");
        session.History.AddRange([user0, reply0, user1, reply1, user2, reply2]);

        ProbeItem item = actions.Wire(new ProbeItem(), reply0);
        items.Add(item);

        await item.RetryCommand.ExecuteAsync(null);

        Assert.Equal(1, confirms.ConfirmCount);
        Assert.Single(session.History);
        Assert.Same(user0, session.History[0]);
        Assert.Equal(1, host.RerunCount);
        Assert.Null(host.LastInput);
    }
}

/// <summary>
/// 装配阶段被取消时把本轮输入补回历史（<see cref="ConversationTurnRunner.RestoreUserMessageOnAbort"/>）。
/// 覆盖「发送/重试后立刻停止」丢消息的修复：正常轮次的取消由 TurnDriver 收尾，
/// 这里只兜它接手之前的空窗。
/// </summary>
public class RestoreUserMessageOnAbortTests
{
    [Fact]
    public void AbortWithSession_AppendsUserMessageToHistory()
    {
        ChatSession session = new() { IsTransient = true };
        ChatMessage message = new(ChatRole.User, "hello");

        ConversationTurnRunner.RestoreUserMessageOnAbort(session, message);

        ChatMessage persisted = Assert.Single(session.History);
        Assert.Same(message, persisted);
    }

    /// <summary>取消前若恰好已写回（框架落的是同一个实例），不能再追加一份</summary>
    [Fact]
    public void AbortWhenMessageAlreadyInHistory_DoesNotDuplicate()
    {
        ChatSession session = new() { IsTransient = true };
        ChatMessage message = new(ChatRole.User, "hello");
        session.History.Add(message);

        ConversationTurnRunner.RestoreUserMessageOnAbort(session, message);

        Assert.Single(session.History);
    }

    [Fact]
    public void AbortWithoutSession_DoesNotThrow()
    {
        ChatMessage message = new(ChatRole.User, "hello");

        ConversationTurnRunner.RestoreUserMessageOnAbort(null, message);
    }

    /// <summary>无输入轮(助手消息重试)装配阶段被取消:没有用户消息要补,不写任何东西</summary>
    [Fact]
    public void AbortWithoutUserMessage_DoesNothing()
    {
        ChatSession session = new() { IsTransient = true };

        ConversationTurnRunner.RestoreUserMessageOnAbort(session, null);

        Assert.Empty(session.History);
    }
}