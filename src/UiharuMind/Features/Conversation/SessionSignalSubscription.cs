/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 视图挂在会话上要接的几路回调。视图构造时建一份，每次挂会话都用它
/// </summary>
/// <param name="ResolveApprovals">后台子代理跑完起的唤醒轮，审批只有本壳接得住（见 <see cref="WakeApprovalHosts"/>）</param>
/// <param name="HistoryAppended">历史被追加了（参数是新增段起点）；可能来自后台线程</param>
/// <param name="HistoryReplaced">历史里某一条被别处原地换掉了；可能来自后台线程</param>
/// <param name="TurnEnded">观察的那一轮结束了</param>
/// <param name="LiveSink">实时内容流的落点</param>
/// <param name="LiveIdentity">落点的身份：自己驱动时按它去重，不会渲染两遍</param>
public sealed record SessionSignalHandlers(
    ApprovalResolver ResolveApprovals,
    Action<int> HistoryAppended,
    Action<int, ChatMessage> HistoryReplaced,
    Action TurnEnded,
    ITurnSink LiveSink,
    object LiveIdentity);

/// <summary>
/// 视图挂在一个会话上的全部信号：钉住历史、唤醒轮审批、历史追加与替换、实时内容流与轮末。
///
/// 会话比视图活得久，挂了必摘——不摘就是一路泄漏到已销毁的视图上，换会话时还会串台。
/// 一个实例只管一个会话，<see cref="Dispose"/> 一次全部摘掉。
/// </summary>
public sealed class SessionSignalSubscription : IDisposable
{
    private readonly ChatSession _session;
    private readonly SessionSignalHandlers _handlers;
    private readonly IDisposable _pin;
    private readonly IDisposable _liveObservation;
    private bool _disposed;

    /// <summary>
    /// 挂上全部信号
    /// </summary>
    /// <param name="session">会话</param>
    /// <param name="handlers">回调</param>
    public SessionSignalSubscription(ChatSession session, SessionSignalHandlers handlers)
    {
        _session = session;
        _handlers = handlers;
        // 钉住它的历史:每个气泡都指着历史里的某一条消息实例,历史被卸掉重载之后
        // 那些引用全部认不回来,编辑/删除/分叉/重试会静默失效(见 SessionResidencyPolicy)
        _pin = SessionManager.Instance.Pin(session.SessionId);
        WakeApprovalHosts.Register(session.SessionId, handlers.ResolveApprovals);
        session.HistoryAppended += handlers.HistoryAppended;
        session.HistoryMessageReplaced += handlers.HistoryReplaced;
        // 这一轮跑到一半才挂上来也补得齐(尚未落盘的那一段会当场补发)
        _liveObservation = session.LiveTurn.Observe(handlers.LiveSink, handlers.LiveIdentity);
        session.LiveTurn.TurnEnded += handlers.TurnEnded;
    }

    /// <summary>摘掉全部信号（重复调用无害）</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WakeApprovalHosts.Unregister(_session.SessionId, _handlers.ResolveApprovals);
        _session.HistoryAppended -= _handlers.HistoryAppended;
        _session.HistoryMessageReplaced -= _handlers.HistoryReplaced;
        _session.LiveTurn.TurnEnded -= _handlers.TurnEnded;
        _liveObservation.Dispose();
        _pin.Dispose();
    }
}
