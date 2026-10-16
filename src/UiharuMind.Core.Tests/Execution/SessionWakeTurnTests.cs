using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Single.GoalMode;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 唤醒轮本轮的审批组合：无人值守开着走本轮自己的通道并通知收卡，关着问壳，都没有为 null
/// </summary>
public class SessionWakeTurnTests
{
    [Fact]
    public void ScopeOff_ReturnsTheHost()
    {
        UnattendedScope scope = new(() => null);
        ApprovalResolver host = _ => Task.FromResult<IReadOnlyList<ChatMessage>>([]);

        Assert.Same(host, SessionWakeTurn.ComposeWakeResolver(host, scope, null));
    }

    [Fact]
    public void NeitherHostNorUnattended_ReturnsNull()
    {
        UnattendedScope scope = new(() => null);

        Assert.Null(SessionWakeTurn.ComposeWakeResolver(null, scope, null));
    }

    /// <summary>开着：本轮通道判据，壳只收卡、不参与判据</summary>
    [Fact]
    public async Task ScopeOn_DecidesThroughTheWakeChannel_AndNotifiesSettled()
    {
        UnattendedScope scope = new(() => GoalModeApproval.Create("s"));
        List<(IReadOnlyList<ToolApprovalRequestContent> Requests, IReadOnlyList<ChatMessage> Responses)> settled = [];
        ApprovalResolver? resolver = SessionWakeTurn.ComposeWakeResolver(null, scope,
            (requests, responses) => settled.Add((requests, responses)));

        Assert.NotNull(resolver);
        ToolApprovalRequestContent request = Request("r1", "Write");
        IReadOnlyList<ChatMessage> responses = await resolver([request]);

        Assert.Single(responses);
        var (requests, notified) = Assert.Single(settled);
        Assert.Same(request, Assert.Single(requests));
        Assert.Same(responses, notified);
    }

    /// <summary>本轮中途撤销：改问壳，不再用冻结的通道</summary>
    [Fact]
    public async Task RevokedMidTurn_FallsBackToTheHost()
    {
        bool on = true;
        UnattendedScope scope = new(() => on ? GoalModeApproval.Create("s") : null);
        bool hostAsked = false;
        ApprovalResolver host = _ =>
        {
            hostAsked = true;
            return Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        };
        ApprovalResolver? resolver = SessionWakeTurn.ComposeWakeResolver(host, scope, null);

        Assert.NotNull(resolver);
        Assert.NotEmpty(await resolver([Request("r1", "Write")]));
        Assert.False(hostAsked);

        on = false;
        Assert.Empty(await resolver([Request("r2", "Write")]));
        Assert.True(hostAsked);
    }

    /// <summary>中途撤销且没有壳：按未决收（与到上限收口同一条路），不抛也不挂起</summary>
    [Fact]
    public async Task RevokedMidTurn_WithoutHost_ClosesAsUnanswered()
    {
        bool on = true;
        UnattendedScope scope = new(() => on ? GoalModeApproval.Create("s") : null);
        ApprovalResolver? resolver = SessionWakeTurn.ComposeWakeResolver(null, scope, null);

        Assert.NotNull(resolver);
        Assert.NotEmpty(await resolver([Request("r1", "Write")]));

        on = false;
        Assert.Empty(await resolver([Request("r2", "Write")]));
    }

    /// <summary>收卡通知必须不影响判据：抛了只记日志，回应照样返回</summary>
    [Fact]
    public async Task SettledThrowing_DoesNotFailTheDecisions()
    {
        UnattendedScope scope = new(() => GoalModeApproval.Create("s"));
        ApprovalResolver? resolver = SessionWakeTurn.ComposeWakeResolver(null, scope,
            (_, _) => throw new InvalidOperationException("收卡炸了"));

        Assert.NotNull(resolver);
        Assert.Single(await resolver([Request("r1", "Write")]));
    }

    private static ToolApprovalRequestContent Request(string id, string tool) =>
        new(id, new FunctionCallContent(id, tool, new Dictionary<string, object?>()));
}
