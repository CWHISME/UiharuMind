/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 视图挂在会话上的信号：会话比视图活得久，摘不干净就是一路泄漏到已销毁的视图上，
/// 换会话时还会串台（旧会话的落盘画进新会话的流里）
/// </summary>
public class SessionSignalSubscriptionTests
{
    private readonly ChatSession _session = new("t", new CharacterData { CharacterId = "t" }) { IsTransient = true };
    private readonly List<int> _replaced = [];

    private SessionSignalSubscription Attach() => new(_session, new SessionSignalHandlers(
        _ => Task.FromResult<IReadOnlyList<ChatMessage>>([]),
        _ => { },
        (index, _) => _replaced.Add(index),
        () => { },
        new ConversationTranscript(new List<ConversationItemBase>(), () => new TextConversationItem(isUser: false)),
        this));

    [Fact]
    public void Attach_RegistersWakeApprovalHost_DisposeUnregisters()
    {
        SessionSignalSubscription signals = Attach();
        Assert.True(WakeApprovalHosts.HasHost(_session.SessionId));

        signals.Dispose();

        Assert.False(WakeApprovalHosts.HasHost(_session.SessionId));
    }

    [Fact]
    public void Dispose_StopsHistoryCallbacks()
    {
        SessionSignalSubscription signals = Attach();
        _session.NotifyHistoryMessageReplaced(3, new ChatMessage(ChatRole.User, "old"));

        signals.Dispose();
        _session.NotifyHistoryMessageReplaced(4, new ChatMessage(ChatRole.User, "old"));

        Assert.Equal([3], _replaced);
    }

    [Fact]
    public void Dispose_Twice_IsHarmless()
    {
        SessionSignalSubscription signals = Attach();

        signals.Dispose();
        signals.Dispose();

        Assert.False(WakeApprovalHosts.HasHost(_session.SessionId));
    }
}
