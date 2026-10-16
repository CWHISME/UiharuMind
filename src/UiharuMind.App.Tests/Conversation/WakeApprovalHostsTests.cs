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
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 唤醒轮的审批谁接：壳、无人值守策略（目标模式的自动同意）、按无头口径拒绝，三者的次序只有一份
/// </summary>
[Collection(WakeApprovalHostsCollection.Name)]
public class WakeApprovalHostsTests : IDisposable
{
    private readonly string _session = "wake-hosts-session";
    private ApprovalResolver? _registered;
    private Action<IReadOnlyList<ToolApprovalRequestContent>, IReadOnlyList<ChatMessage>>? _settled;

    /// <summary>跑完把登记清掉：这是进程级状态，别的测试类还在用</summary>
    public void Dispose()
    {
        if (_registered != null) WakeApprovalHosts.Unregister(_session, _registered);
        if (_settled != null) WakeApprovalHosts.UnregisterSettled(_session, _settled);
        WakeApprovalHosts.SetUnattendedApprovals(null);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void NoHostAndNoPolicy_NobodyToAsk()
    {
        Assert.Null(WakeApprovalHosts.ResolveHost(_session));
        Assert.Null(WakeApprovalHosts.UnattendedFor(_session));
    }

    /// <summary>壳开着：唤醒轮问壳</summary>
    [Fact]
    public void HostRegistered_TheWakeTurnAsksTheShell()
    {
        WakeApprovalHosts.SetUnattendedApprovals(_ => null);
        bool asked = false;
        ApprovalResolver host = _ =>
        {
            asked = true;
            return Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        };

        WakeApprovalHosts.Register(_session, host);
        _registered = host; //Dispose 据此摘掉：进程级字典残留会污染同类其他用例

        Assert.Same(host, WakeApprovalHosts.ResolveHost(_session));
        Assert.True(WakeApprovalHosts.HasHost(_session));
        Assert.False(asked);
    }

    /// <summary>壳不在（会话没开着）：无人值守的策略照样取得到，不再按无头口径拒绝</summary>
    [Fact]
    public void NoHost_UnattendedPolicyAnswers()
    {
        ApprovalResolver unattended = _ => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        WakeApprovalHosts.SetUnattendedApprovals(id => id == _session ? unattended : null);

        Assert.Null(WakeApprovalHosts.ResolveHost(_session));
        Assert.Same(unattended, WakeApprovalHosts.UnattendedFor(_session));
        Assert.False(WakeApprovalHosts.HasHost(_session));
    }

    /// <summary>
    /// 「问不问人」与「有没有壳」是两回事：同一天会话可以既开着、又勾了自动同意。
    /// 窗口据此把卡片就地收掉，唤醒轮据此知道背后没有人（见 SessionWakePolicy.AttendedSource）
    /// </summary>
    [Fact]
    public void HostAndPolicy_BothAnswersStayAvailable()
    {
        ApprovalResolver host = _ => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        ApprovalResolver unattended = _ => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        WakeApprovalHosts.SetUnattendedApprovals(_ => unattended);
        _registered = host;
        WakeApprovalHosts.Register(_session, host);

        Assert.Same(host, WakeApprovalHosts.ResolveHost(_session));
        Assert.Same(unattended, WakeApprovalHosts.UnattendedFor(_session));
        Assert.True(WakeApprovalHosts.HasHost(_session));

        WakeApprovalHosts.Unregister(_session, host);
        _registered = null;

        Assert.Null(WakeApprovalHosts.ResolveHost(_session));
        Assert.Same(unattended, WakeApprovalHosts.UnattendedFor(_session));
        Assert.False(WakeApprovalHosts.HasHost(_session));
    }

    /// <summary>没有会话标识就问不出名堂：装配完成之前那一轮正是这个状态</summary>
    [Fact]
    public void NoSessionId_NothingToAsk()
    {
        WakeApprovalHosts.SetUnattendedApprovals(_ => throw new InvalidOperationException("不该被问到"));
        ApprovalResolver host = _ => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        _registered = host;
        WakeApprovalHosts.Register(_session, host);

        Assert.Null(WakeApprovalHosts.ResolveHost(null));
        Assert.Null(WakeApprovalHosts.ResolveHost(string.Empty));
        Assert.Null(WakeApprovalHosts.UnattendedFor(null));
        Assert.Null(WakeApprovalHosts.UnattendedFor(string.Empty));
        Assert.False(WakeApprovalHosts.HasHost(null));
    }

    /// <summary>只认壳：判据与收卡分开走时，唤醒轮拿壳只问用户点的那些</summary>
    [Fact]
    public void ResolveHost_ReturnsOnlyTheShell()
    {
        WakeApprovalHosts.SetUnattendedApprovals(_ => throw new InvalidOperationException("不该被问到"));
        ApprovalResolver host = _ => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        _registered = host;
        WakeApprovalHosts.Register(_session, host);

        Assert.Same(host, WakeApprovalHosts.ResolveHost(_session));
        Assert.Null(WakeApprovalHosts.ResolveHost("other-session"));
        Assert.Null(WakeApprovalHosts.ResolveHost(null));
    }

    /// <summary>收卡回调与壳同一生命周期：摘壳时一并摘掉，别家会话不受影响</summary>
    [Fact]
    public void SettledHandler_FollowsTheShellLifetime()
    {
        int settled = 0;
        _settled = (_, _) => settled++;
        WakeApprovalHosts.RegisterSettled(_session, _settled);

        WakeApprovalHosts.SettledFor(_session)?.Invoke([], []);
        Assert.Equal(1, settled);
        Assert.Null(WakeApprovalHosts.SettledFor("other-session"));

        WakeApprovalHosts.UnregisterSettled(_session, _settled);
        _settled = null;

        Assert.Null(WakeApprovalHosts.SettledFor(_session));
    }
}
