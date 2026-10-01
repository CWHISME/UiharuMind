/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 一轮的装配阶段：驱动还没接手的那一段，停止必须停得下来、输入不能丢、出错要落到会话流里
/// </summary>
public class ConversationTurnRunnerTests
{
    private sealed class FakeHost : IConversationTurnHost
    {
        public ChatSession Session { get; } =
            new("t", new CharacterData { CharacterId = "t" }) { IsTransient = true };

        public Func<CancellationToken, Task<ChatSession>> Ensure { get; set; } = _ => throw new NotSupportedException();
        public List<string> Errors { get; } = [];
        public int PreparingChanges { get; private set; }

        public string? CurrentSessionId => null;
        public ChatSession? CurrentSession => Session;

        public Task<ChatSession> EnsureSessionAsync(string titleSeed, CancellationToken cancellationToken) =>
            Ensure(cancellationToken);

        public void OnSessionEnsured() { }
        public void NotifyPreparingChanged() => PreparingChanges++;
        public void ShowError(string message) => Errors.Add(message);
    }

    private static ConversationTurnRunner NewRunner(FakeHost host)
    {
        List<ConversationItemBase> items = [];
        ConversationTranscript transcript = new(items, () => new TextConversationItem(isUser: false));
        return new ConversationTurnRunner(host, new TurnDriver(transcript, new TurnUsageLedger()), transcript);
    }

    /// <summary>装配期间按停止：轮次不起，本轮输入补回历史，装配态在前后各通知一次</summary>
    [Fact]
    public async Task CancelWhilePreparing_RestoresInputAndEndsPreparing()
    {
        FakeHost host = new() { Ensure = async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        } };
        ConversationTurnRunner runner = NewRunner(host);
        ChatMessage input = new(ChatRole.User, "hello");

        Task run = runner.RunAsync(input, "hello");
        Assert.True(runner.IsPreparing);

        runner.Cancel();
        await run;

        Assert.False(runner.IsPreparing);
        Assert.Same(input, Assert.Single(host.Session.History));
        Assert.Equal(2, host.PreparingChanges);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task EnsureFails_ShowsErrorAndEndsPreparing()
    {
        FakeHost host = new() { Ensure = _ => throw new InvalidOperationException("session broken") };
        ConversationTurnRunner runner = NewRunner(host);

        await runner.RunAsync(new ChatMessage(ChatRole.User, "hello"), "hello");

        Assert.False(runner.IsPreparing);
        Assert.Equal("session broken", Assert.Single(host.Errors));
        Assert.Empty(host.Session.History);
    }

    /// <summary>没在装配时停止什么都不碰（驱动自己的取消是空操作）</summary>
    [Fact]
    public void CancelWhenIdle_IsHarmless()
    {
        FakeHost host = new();
        ConversationTurnRunner runner = NewRunner(host);

        runner.Cancel();
        runner.CancelPreparing();

        Assert.False(runner.IsPreparing);
        Assert.Equal(0, host.PreparingChanges);
    }
}
