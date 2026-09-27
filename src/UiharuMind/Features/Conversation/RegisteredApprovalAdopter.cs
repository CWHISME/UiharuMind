/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 会话窗口认领<b>登记在册</b>的审批卡：子会话（嵌套审批，见 ADR 0021）与群成员（他在群里那一轮）的审批
/// 不走本窗口自己那一轮的回应口，而是登记在 <see cref="SessionApprovalRegistry"/>，由画出来的卡认领后回应。
/// 普通会话自己的卡认不到登记（没人登记过），原样走本轮的回应口
/// </summary>
public sealed class RegisteredApprovalAdopter : IDisposable
{
    private readonly ConversationTranscript _transcript;
    private readonly Func<string?> _sessionIdSource;
    private readonly Func<ChatSession?> _sessionSource;

    /// <summary>
    /// 构造并挂上「画出了审批卡」与「登记进来了审批」两头的通知
    /// </summary>
    /// <param name="transcript">实时流转录器（卡由它画）</param>
    /// <param name="sessionIdSource">当前会话标识（后台线程上也可读）</param>
    /// <param name="sessionSource">当前会话本体</param>
    public RegisteredApprovalAdopter(ConversationTranscript transcript, Func<string?> sessionIdSource,
        Func<ChatSession?> sessionSource)
    {
        _transcript = transcript;
        _sessionIdSource = sessionIdSource;
        _sessionSource = sessionSource;
        _transcript.ApprovalRequestCreated += OnApprovalRequestCreated;
        // 登记与画卡在两个线程上各走各的,谁先都有可能——登记侧也喊一声,让已经画出来的卡回头认领
        SessionApprovalRegistry.Instance.PendingAdded += OnApprovalsPending;
    }

    /// <summary>
    /// 观察别人驱动的那一轮时，审批卡放不放行。
    ///
    /// 要放的情形：登记在册的（子会话、群成员），以及本会话的<b>唤醒轮</b>
    /// ——那一轮由后台驱动，回应口就登记在本壳上（见 <see cref="WakeApprovalHosts"/>）。
    /// 其余普通会话的观察窗照旧丢弃：那张卡画出来也按不动，还会一直挂在待决清单上。
    /// </summary>
    /// <returns>是否放行</returns>
    public bool AllowsObservedApproval() =>
        _sessionSource() is { } session && (AdoptsRegistered(session) || WakeApprovalHosts.HasHost(session.SessionId));

    /// <summary>摘掉订阅</summary>
    public void Dispose()
    {
        _transcript.ApprovalRequestCreated -= OnApprovalRequestCreated;
        SessionApprovalRegistry.Instance.PendingAdded -= OnApprovalsPending;
    }

    private static bool AdoptsRegistered(ChatSession session) => session.IsSubSession || session.IsGroupMember;

    /// 认领一张登记在册的审批卡，并跟着那次审批的最终结果走：别处（群的待审批条）先点了，
    /// 或窗口打开前就已经批过了，这张卡都收起按钮、显示那个决定——不然它一直挂着像还在等，
    /// 轮末还会被按拒绝收视觉，把批准过的显示成拒绝
    private static void Adopt(string sessionId, ApprovalRequestItem item)
    {
        // 先取结果再认领：已经决出的（晚打开的窗口补画出来的卡）认领不到，但结果查得到
        if (SessionApprovalRegistry.Instance.DecisionOf(sessionId, item.Request) is not { } decision) return;
        SessionApprovalRegistry.Instance.TryAdopt(sessionId, item.Request, item.Response);
        decision.ContinueWith(done => Dispatcher.UIThread.Post(() => item.MarkDecidedElsewhere(done.Result)),
            CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private void OnApprovalRequestCreated(ApprovalRequestItem item)
    {
        if (_sessionSource() is not { } session || !AdoptsRegistered(session)) return;
        Adopt(session.SessionId, item);
    }

    /// <summary>
    /// 有审批登记进来了：把本窗口已经画出来的待决卡片再认领一遍。
    ///
    /// 认领两头都要做——卡片可能先于登记诞生（内容流转发到界面是 Post 出去的），
    /// 也可能后于登记诞生（晚开的窗口从流回放里拿到同一批请求）。重复认领无害：
    /// 决定先到先得。<b>可能来自后台线程</b>，所以 marshal 之后再动界面。
    /// </summary>
    private void OnApprovalsPending(string sessionId)
    {
        if (sessionId != _sessionIdSource()) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_sessionSource() is not { } session || !AdoptsRegistered(session)) return;
            foreach (ApprovalRequestItem item in _transcript.PendingApprovals.ToList())
            {
                Adopt(session.SessionId, item);
            }
        });
    }
}
