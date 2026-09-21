using System;
using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 重试入口的挂载规则与装配中断时的消息补回。
///
/// 重试语义是「从这条输入起重新生成」：用户消息以自己为锚；助手消息回落到它前面
/// 最近的用户提问（点助手回复重试 = 从对应提问重跑）。旁白（开场白/子代理报告）
/// 没有对应的提问，不给重试。
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
        public void Rerun(ChatMessage input) => RerunCount++;
        public void NotifySessionsChanged() { }
        public void NotifyItemsWired() { }
    }

    private static ChatSession TransientSession() => new() { IsTransient = true };

    [Fact]
    public void WiringAssistantMessage_ProvidesRetry()
    {
        ChatSession session = TransientSession();
        ConversationItemActions actions = new(new ObservableCollection<ConversationItemBase>(), new RecordingHost(session));

        ChatMessage reply = new(ChatRole.Assistant, "回答");
        ProbeItem item = actions.Wire(new ProbeItem(), reply);

        Assert.True(item.CanRetry);
    }

    [Fact]
    public void WiringUserMessage_StillProvidesRetry()
    {
        ChatSession session = TransientSession();
        ConversationItemActions actions = new(new ObservableCollection<ConversationItemBase>(), new RecordingHost(session));

        ChatMessage question = new(ChatRole.User, "提问");
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
    /// 助手消息重试但前面没有用户提问（例如开场白后直接是回复）：锚点不存在，
    /// 历史与条目都不动、不触发重跑——不能把开场白当提问删了重跑。
    /// </summary>
    [Fact]
    public void RetryingAssistantWithoutAnchorUser_DoesNothing()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        RecordingHost host = new(session);
        ConversationItemActions actions = new(items, host);

        ChatMessage reply = new(ChatRole.Assistant, "开场回复");
        session.History.Add(reply);
        ProbeItem item = actions.Wire(new ProbeItem(), reply);
        items.Add(item);

        item.RetryCommand.Execute(null);

        Assert.Single(session.History);
        Assert.Same(reply, session.History[0]);
        Assert.Single(items); //条目没被删
        Assert.Equal(0, host.RerunCount);
    }

    /// <summary>
    /// 助手消息前面的用户消息是旁白（开场白）时不算锚点：开场白不是提问。
    /// </summary>
    [Fact]
    public void RetryingAssistantWithOnlyNarrationBefore_DoesNothing()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        RecordingHost host = new(session);
        ConversationItemActions actions = new(items, host);

        ChatMessage narration = new(ChatRole.User, "开场白")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Narration] = true },
        };
        ChatMessage reply = new(ChatRole.Assistant, "开场回复");
        session.History.AddRange([narration, reply]);
        ProbeItem item = actions.Wire(new ProbeItem(), reply);
        items.Add(item);

        item.RetryCommand.Execute(null);

        Assert.Equal(2, session.History.Count);
        Assert.Equal(0, host.RerunCount);
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
}
