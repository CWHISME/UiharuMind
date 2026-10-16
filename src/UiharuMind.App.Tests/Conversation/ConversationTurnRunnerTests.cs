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
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Single.GoalMode;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;
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
        public ChatSession Session { get; init; } =
            new("t", new CharacterData { CharacterId = "t" }) { IsTransient = true };

        public Func<CancellationToken, Task<ChatSession>> Ensure { get; set; } = _ => throw new NotSupportedException();
        public List<string> Errors { get; } = [];
        public int PreparingChanges { get; private set; }

        public string? CurrentSessionId { get; init; }
        public ChatSession? CurrentSession => Session;

        public Task<ChatSession> EnsureSessionAsync(string titleSeed, CancellationToken cancellationToken) =>
            Ensure(cancellationToken);

        public void OnSessionEnsured() { }
        public void NotifyPreparingChanged() => PreparingChanges++;
        public void ShowError(string message) => Errors.Add(message);
    }

    private static ConversationTurnRunner NewRunner(FakeHost host,
        Func<string?, ApprovalResolver?>? unattended = null)
    {
        List<ConversationItemBase> items = [];
        ConversationTranscript transcript = new(items, () => new TextConversationItem(isUser: false));
        return new ConversationTurnRunner(host, new TurnDriver(transcript, new TurnUsageLedger()), transcript,
            unattended);
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

    /// <summary>
    /// 群成员会话私聊撞上群轮（ADR 0063）：装配之前就叫停群轮、等它放闸——群轮整轮持着执行者的锁，
    /// 先装配就会卡在那把锁上，根本走不到叫停
    /// </summary>
    [Fact]
    public async Task GroupMemberSession_PreemptsTheGroupTurnBeforeAssembling()
    {
        ChatSession member = new("m", new CharacterData { CharacterId = "m" }) { GroupId = Guid.NewGuid().ToString() };
        SessionManager.Instance.Add(member);
        try
        {
            bool ensured = false;
            FakeHost host = new()
            {
                Session = member,
                CurrentSessionId = member.SessionId,
                Ensure = _ =>
                {
                    ensured = true;
                    throw new InvalidOperationException("stop here");
                },
            };
            PreemptibleTurn group = GroupMemberTurnGate.TryEnterPreemptible(member.SessionId, CancellationToken.None)!;

            Task run = NewRunner(host).RunAsync(new ChatMessage(ChatRole.User, "hello"), "hello");

            Assert.True(group.Token.IsCancellationRequested);
            Assert.False(ensured); //群轮还没放闸：装配还没开始
            group.Dispose();
            await run;
            Assert.True(ensured);
        }
        finally
        {
            SessionManager.Instance.Delete(member.SessionId);
        }
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

    /// <summary>
    /// 会话勾了「自动同意审批」（用户确认过危险度）：这一轮的审批不等用户点，直接按同意回应，
    /// 回应带上自动同意的理由（历史据此认出它不是人点的）
    /// </summary>
    [Fact]
    public async Task UnattendedApprovals_AnswersWithoutWaitingForTheUser()
    {
        ChatSession session = new("g", new CharacterData { CharacterId = "g" }) { IsTransient = true };
        FakeHost host = new() { Session = session, CurrentSessionId = session.SessionId };
        ConversationTurnRunner runner = NewRunner(host, id => GoalModeApproval.Create(id ?? string.Empty));

        ToolApprovalRequestContent request =
            new("r1", new FunctionCallContent("c1", "Write", new Dictionary<string, object?>()));

        IReadOnlyList<ChatMessage> responses = await runner.ResolveApprovalsFor(session.SessionId)([request]);

        ToolApprovalResponseContent content =
            Assert.Single(Assert.Single(responses).Contents.OfType<ToolApprovalResponseContent>());
        Assert.True(content.Approved);
        Assert.Equal(GoalModeApproval.Reason, content.Reason);
    }

    /// <summary>
    /// 同一轮里多批审批共用一个无人值守通道：每批都新建的话轮次上限永不触发，
    /// 前台这一轮就没了审批侧闸门（计数记在通道里，一轮只取一次）。
    /// 开关本身每批现问（工厂调用次数不固定），不断的是通道
    /// </summary>
    [Fact]
    public async Task UnattendedResolver_IsReusedWithinATurn_SoRoundCapApplies()
    {
        ChatSession session = new("g", new CharacterData { CharacterId = "g" }) { IsTransient = true };
        FakeHost host = new() { Session = session, CurrentSessionId = session.SessionId };
        ConversationTurnRunner runner = NewRunner(host, id => GoalModeApproval.Create(id ?? string.Empty));

        ToolApprovalRequestContent request =
            new("r1", new FunctionCallContent("c1", "Write", new Dictionary<string, object?>()));
        ApprovalResolver resolve = runner.ResolveApprovalsFor(session.SessionId);
        IReadOnlyList<ChatMessage> last = [];
        int batches = 0;
        for (; batches < 20; batches++)
        {
            last = await resolve([request]);
            if (last.Count == 0) break;
        }

        Assert.True(batches < 20, "round cap should end the run instead of approving forever");
        Assert.Empty(last);
    }

    /// <summary>
    /// 中途撤销立即生效：开关关掉后不再自动同意，回落到用户点选（空 transcript 里没有卡，直接返回空）
    /// </summary>
    [Fact]
    public async Task RevokedMidTurn_FallsBackInsteadOfAutoApproving()
    {
        ChatSession session = new("g", new CharacterData { CharacterId = "g" }) { IsTransient = true };
        FakeHost host = new() { Session = session, CurrentSessionId = session.SessionId };
        bool on = true;
        ConversationTurnRunner runner = NewRunner(host, _ => on ? GoalModeApproval.Create("g") : null);
        ApprovalResolver resolve = runner.ResolveApprovalsFor(session.SessionId);

        ToolApprovalRequestContent request =
            new("r1", new FunctionCallContent("c1", "Write", new Dictionary<string, object?>()));
        Assert.NotEmpty(await resolve([request]));

        on = false;
        Assert.Empty(await resolve([request]));
    }
}
