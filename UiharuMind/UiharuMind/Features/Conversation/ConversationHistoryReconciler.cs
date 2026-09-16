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
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 历史对账的宿主机位：对账器只吃这几个窄依赖，不反向持有视图模型
/// （与 <see cref="ConversationItemWindowTrimmer"/> 同一层、同一做法）。
/// </summary>
public interface IConversationReconcileHost
{
    /// <summary>自己那一轮是否正在跑</summary>
    bool IsOwnTurnRunning { get; }

    /// <summary>会话是否正在加载</summary>
    bool IsSessionLoading { get; }

    /// <summary>当前会话；无会话为 null</summary>
    ChatSession? CurrentSession { get; }

    /// <summary>「加载更早」的状态位，对账改写窗口后同步它</summary>
    bool HasEarlierMessages { get; set; }

    /// <summary>运行态登记处的空闲判据（唤醒轮占位与直播置位之间有一瞬空窗，只认它）</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>忙则 true</returns>
    bool IsSessionBusy(string sessionId);

    /// <summary>回放一段历史。定格重放（<c>liveTail: false</c>）：对账只在没人跑时动手，
    /// 此时尚无结果的调用就该按「历史里没有结果」收口，转圈卡才是谎报</summary>
    /// <param name="history">完整历史</param>
    /// <param name="from">起始下标</param>
    /// <param name="to">结束下标（不含）</param>
    /// <returns>装配好的条目</returns>
    List<ConversationItemBase> BuildItems(IReadOnlyList<ChatMessage> history, int from, int to);

    /// <summary>用量文案跟着新画出来的条目刷一次</summary>
    void RefreshTokenUsage();
}

/// <summary>
/// 界面条目与历史的<b>对账执行处</b>：从 <see cref="ConversationViewModel"/> 拆出来，
/// 那个类已经两千多行，对账不该再往里堆。
///
/// 管的事只有一件：交回报告 / 唤醒回复已落盘、但实时通道（历史追加信号、内容流观察）
/// 都没把那几条画出来时，按历史补齐（实机见过：落盘与唤醒轮都在，
/// 后续报告与回复却要切会话才看得见）。
///
/// 只在<b>没人跑</b>时动手——有轮在跑时归流式与观察轮结束检查管，
/// 此时重放会跟直播打架。触发点是「名下委派全部交回」（pending 归零）、
/// 「本会话转空闲」与「观察轮结束」：都是“本该有新内容、也该安静了”的时刻。
/// 顺序对不上走全量重放，尾部漏画走增量追加。
///
/// 幂等：对得上时直接返回；增量追加不 Clear 窗口、不重解已渲染的位图，
/// 用户翻出来的更早消息原位不动。
/// </summary>
public sealed class ConversationHistoryReconciler
{
    private readonly ObservableCollection<ConversationItemBase> _items;
    private readonly HistoryWindow _window;
    private readonly IConversationReconcileHost _host;

    /// <param name="items">界面条目集合（就地对账）</param>
    /// <param name="window">与加载期、裁剪共用的同一个渲染窗口</param>
    /// <param name="host">宿主机位</param>
    public ConversationHistoryReconciler(ObservableCollection<ConversationItemBase> items, HistoryWindow window,
        IConversationReconcileHost host)
    {
        _items = items;
        _window = window;
        _host = host;
    }

    /// <summary>
    /// 对账一次。<b>必须在 UI 线程上调用</b>——读写的是界面条目集合，
    /// 跨线程的 marshal 由调用方（视图模型）负责。
    /// </summary>
    /// <param name="reason">触发来源，进日志，方便区分是哪条路兜住的</param>
    public void Reconcile(string reason)
    {
        if (_host.IsOwnTurnRunning) return;
        if (_host.IsSessionLoading) return;
        if (_host.CurrentSession is not { } session) return;
        // 登记处是“空闲”的唯一可靠定义，先查它（被拦下的对账由「session idle」在轮结束后再来一次）
        if (_host.IsSessionBusy(session.SessionId)) return;
        if (session.LiveTurn.IsTurnRunning) return;

        if (ConversationOrderCheck.FindDivergence(_items, session.History) is { } divergence)
        {
            Log.Warning($"Conversation items diverged from history ({divergence}); "
                        + $"replaying the window ({reason}).");
            ReplayPreservingWindow(session.History);
            return;
        }

        int missing = ConversationOrderCheck.FindMissingTail(_items, session.History);
        if (missing == 0) return;
        Log.Warning($"Conversation items diverged from history "
                    + $"({missing} trailing message(s) not drawn); appending the tail ({reason}).");
        foreach (ConversationItemBase item in _host.BuildItems(session.History,
                     session.History.Count - missing, session.History.Count))
        {
            _items.Add(item);
        }

        _host.RefreshTokenUsage();
    }

    /// <summary>全量重放当前窗口。旧条目的位图随条目走，直接 Clear 会泄漏：先摘绑定再释放</summary>
    private void ReplayPreservingWindow(IReadOnlyList<ChatMessage> history)
    {
        ConversationItemBase[] discarded = _items.ToArray();
        _items.Clear();
        foreach (ConversationItemBase item in discarded) item.ReleaseImages();

        // 保住用户已翻出的窗口：HistoryWindow.Reset 会把起点打回首屏，
        // 全量重放就把「加载更早」取回来的那几段又弄丢了。历史只增不减时起点原位；
        // 被压缩或删除变短时钳到末尾。SetStart 顺带清掉首屏欠账——这一段整体重画，
        // 欠的正是其中一部分，留着反而会在稍后重复前插。
        int from = Math.Min(_window.Start, history.Count);
        _window.SetStart(from);
        foreach (ConversationItemBase item in _host.BuildItems(history, from, history.Count))
        {
            _items.Add(item);
        }

        _host.HasEarlierMessages = _window.HasEarlier;
        _host.RefreshTokenUsage();
    }
}
