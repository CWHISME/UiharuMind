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

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 重试入口的挂载规则与确认弹窗。
///
/// 重试语义是「从这条消息起重新生成」：用户消息以自己为锚（删掉它及之后，重跑一轮）；
/// 助手消息同样以自己为锚（删掉它及之后，由无输入轮续写新回复）——<b>不再回溯到它前面
/// 最近的提问重跑整轮</b>，否则重试一条靠前的回复会把提问和更早的对话一起卷进去。
/// 旁白（开场白/子代理报告）不提供重试。助手消息重试要删的条数较多时先弹确认。
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

    /// <summary>确认 stub;记录是否问过、以及拒绝时的行为</summary>
    private sealed class ConfirmStub : IMessageService
    {
        public int ConfirmCount { get; private set; }
        public bool Result { get; init; } = true;
        public string? LastMessage { get; private set; }

        public Task<bool> ConfirmAsync(string message, string? title = null, CancellationToken ct = default)
        {
            ConfirmCount++;
            LastMessage = message;
            return Task.FromResult(Result);
        }
        public Task ShowInfoAsync(string message, string? title = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ShowWarningAsync(string message, string? title = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task ShowErrorAsync(string message, string? title = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<EConfirmChoice> ConfirmWithCancelAsync(string message, string? title = null,
            CancellationToken ct = default) => Task.FromResult(EConfirmChoice.Yes);
        public void ShowNotification(string message, string? title = null,
            MessageSeverity severity = MessageSeverity.Information, TimeSpan? duration = null) { }
    }

    private static ChatSession TransientSession() => new() { IsTransient = true };

    private static ChatMessage UserMessage(string text) => new(ChatRole.User, text);
    private static ChatMessage AssistantText(string text) => new(ChatRole.Assistant, text);

    [Fact]
    public void WiringAssistantMessage_ProvidesRetry()
    {
        ChatSession session = TransientSession();
        ConversationItemActions actions = new(new ObservableCollection<ConversationItemBase>(), new RecordingHost(session));

        ChatMessage reply = AssistantText("回答");
        ProbeItem item = actions.Wire(new ProbeItem(), reply);

        Assert.True(item.CanRetry);
    }

    [Fact]
    public void WiringUserMessage_StillProvidesRetry()
    {
        ChatSession session = TransientSession();
        ConversationItemActions actions = new(new ObservableCollection<ConversationItemBase>(), new RecordingHost(session));

        ChatMessage question = UserMessage("提问");
        ProbeItem item = actions.Wire(new ProbeItem(), question);

        Assert.True(item.CanRetry);
    }

    [Fact]
    public void WiringNarration_DoesNotProvideRetry()
    {
        ChatSession session = TransientSession();
        ConversationItemActions actions = new(new ObservableCollection<ConversationItemBase>(), new RecordingHost(session));

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
        ConversationItemActions actions = new(items, host);

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
        ConversationItemActions actions = new(items, host);

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
    /// 助手消息重试要删的条数达到阈值才弹确认：重试第一条回复、后面还有一大段时，
    /// 不能静默全删。确认拒绝则历史与条目都不动。
    /// </summary>
    [Fact]
    public async Task RetryingAssistant_ConfirmShownWhenDoomCountLarge_AndRespected()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        RecordingHost host = new(session);
        ConfirmStub confirms = new() { Result = false }; //点「取消」
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

        // 重试 reply0 要删 5 条,弹了确认,用户取消 → 什么都不动
        Assert.Equal(1, confirms.ConfirmCount);
        Assert.Equal(6, session.History.Count);
        Assert.Single(items);
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
        ConfirmStub confirms = new() { Result = true };
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

    /// <summary>
    /// 只删该条自己（或很少的几条）时不打扰：问答轮重试最新回复不弹确认。
    /// </summary>
    [Fact]
    public async Task RetryingAssistant_SmallDoomCount_NoConfirm()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        RecordingHost host = new(session);
        ConfirmStub confirms = new();
        ConversationItemActions actions = new(items, host, confirms);

        ChatMessage user0 = UserMessage("问题1");
        ChatMessage reply0 = AssistantText("回答1");
        ChatMessage user1 = UserMessage("问题2");
        ChatMessage reply1 = AssistantText("回答2");
        session.History.AddRange([user0, reply0, user1, reply1]);

        ProbeItem item = actions.Wire(new ProbeItem(), reply1);
        items.Add(item);

        await item.RetryCommand.ExecuteAsync(null);

        Assert.Equal(0, confirms.ConfirmCount); //只删 1 条,不弹
        Assert.Equal(3, session.History.Count);
        Assert.Same(user0, session.History[0]);
        Assert.Same(reply0, session.History[1]);
        Assert.Same(user1, session.History[2]);
        Assert.Equal(1, host.RerunCount);
        Assert.Null(host.LastInput);
    }
}

/// <summary>
/// 装配阶段被取消时把本轮输入补回历史（<see cref="ConversationViewModel.RestoreUserMessageOnAbort"/>）。
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

        ConversationViewModel.RestoreUserMessageOnAbort(session, message);

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

        ConversationViewModel.RestoreUserMessageOnAbort(session, message);

        Assert.Single(session.History);
    }

    [Fact]
    public void AbortWithoutSession_DoesNotThrow()
    {
        ChatMessage message = new(ChatRole.User, "hello");

        ConversationViewModel.RestoreUserMessageOnAbort(null, message);
    }

    /// <summary>无输入轮(助手消息重试)装配阶段被取消:没有用户消息要补,不写任何东西</summary>
    [Fact]
    public void AbortWithoutUserMessage_DoesNothing()
    {
        ChatSession session = new() { IsTransient = true };

        ConversationViewModel.RestoreUserMessageOnAbort(session, null);

        Assert.Empty(session.History);
    }
}