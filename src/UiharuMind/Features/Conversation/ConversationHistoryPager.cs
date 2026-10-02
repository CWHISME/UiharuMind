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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.Diagnostics;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 会话流的历史开窗：首屏回放、往前续窗、补齐首窗，以及运行期与切走之后的裁剪。
///
/// 会话流没有虚拟化，「界面上留多少条目」就是排版与内存的成本，这件事只在这里管。
/// 对外只给意图（回放、往前翻、按预算收），窗口怎么开、上限是多少都不外露。
/// 搜索跳到旧消息之后「脱离末尾」的那一组动作在 <c>ConversationHistoryPager.Detach.cs</c>。
///
/// 三个状态位都是窗口的投影，只在 <see cref="SyncFlags"/> 一处刷新，不各自赋值
/// </summary>
public sealed partial class ConversationHistoryPager : ObservableObject
{
    /// <summary>窗口之前还有没渲染的历史（顶部「加载更早」的可见性）</summary>
    [ObservableProperty] private bool _hasEarlierMessages;

    /// <summary>
    /// 本会话是否已经续过至少一窗更早的消息。只用来决定顶部那行「已到会话开头」显不显示——
    /// 短会话本来就没有更早的消息，一进来就挂那一行是噪音。
    /// </summary>
    [ObservableProperty] private bool _hasLoadedEarlier;

    /// <summary>
    /// 窗口脱离了历史末尾：截断重载到旧消息附近之后为真（「回到最新」的可见性、跟底暂停、对账与裁剪让路）。
    /// 为真期间新消息不往窗口里追加，见 <see cref="AppendPersisted"/>
    /// </summary>
    [ObservableProperty] private bool _hasLaterMessages;

    private readonly ObservableCollection<ConversationItemBase> _items;
    private readonly ConversationHistoryRenderer _renderer;
    private readonly Func<IReadOnlyList<ChatMessage>> _historySource;
    private readonly Func<bool> _isDisplayed;
    private readonly Func<bool> _isTurnRunning;
    private readonly ConversationItemWindowTrimmer _trimmer;

    /// <summary>渲染窗口。对账重放要原位保住用户已翻出的那一段，所以与它共用</summary>
    internal HistoryWindow Window { get; } = new();

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="items">会话条目集合（界面绑的那一份）</param>
    /// <param name="renderer">把历史画成条目</param>
    /// <param name="historySource">当前完整历史（现取现用：中途换会话也能跟上）</param>
    /// <param name="isDisplayed">本实例此刻是否显示在界面上</param>
    /// <param name="isStuckToBottom">界面是否跟在底部；用户上滚在读时不裁，免得正看的内容被抽走</param>
    /// <param name="isTurnRunning">有一轮正往这个会话流内容（回到最新时末尾那次工具调用的结果可能还在路上）</param>
    public ConversationHistoryPager(ObservableCollection<ConversationItemBase> items,
        ConversationHistoryRenderer renderer, Func<IReadOnlyList<ChatMessage>> historySource,
        Func<bool> isDisplayed, Func<bool> isStuckToBottom, Func<bool> isTurnRunning)
    {
        _items = items;
        _renderer = renderer;
        _historySource = historySource;
        _isDisplayed = isDisplayed;
        _isTurnRunning = isTurnRunning;
        // 不在界面上的实例没有会被抽走的视口,照裁——后台跑着的那个正是最该裁的
        _trimmer = new ConversationItemWindowTrimmer(items, Window, historySource,
            () => !isDisplayed() || isStuckToBottom());
    }

    /// <summary>
    /// 回放历史，只渲染最近的<b>首屏</b>。凑够一窗的那几条由 <see cref="FillFirstWindow"/> 在界面可见之后补，
    /// 更早的由 <see cref="LoadEarlier"/> 按批前插——非虚拟化列表靠数据开窗保住长会话性能
    /// </summary>
    /// <param name="messages">完整历史</param>
    /// <param name="liveTail">有一轮正跑着：历史末尾的工具调用结果多半正在路上，不能按「没有结果」收掉</param>
    public void Replay(IReadOnlyList<ChatMessage> messages, bool liveTail)
    {
        (int from, int to) = Window.Reset(messages.Count);
        SyncFlags();
        _renderer.Append(messages, from, to, liveTail);
    }

    /// <summary>
    /// 向前扩展一窗历史。滚动位置的保持由调用方负责
    /// </summary>
    /// <returns>真的前插了条目返回 true（调用方据此决定要不要补偿视口）</returns>
    public bool LoadEarlier()
    {
        IReadOnlyList<ChatMessage> history = _historySource();
        if (Window.Extend(history.Count) is not { } range)
        {
            SyncFlags();
            return false;
        }

        Prepend(history, range);
        HasLoadedEarlier = true;
        return true;
    }

    /// <summary>
    /// 把首屏补齐到整窗。切会话时只回放首屏，省下的那几条布局是「点下去到看见」这段延迟的大头；
    /// 界面贴底可见之后由视图层在空闲时调用。滚动位置的保持同样由调用方负责。
    ///
    /// 这不是用户往前翻，所以不置 <see cref="HasLoadedEarlier"/>
    /// </summary>
    /// <returns>真的补了条目返回 true</returns>
    public bool FillFirstWindow()
    {
        IReadOnlyList<ChatMessage> history = _historySource();
        if (Window.FillFirstWindow(history.Count) is not { } range)
        {
            SyncFlags();
            return false;
        }

        Prepend(history, range);
        return true;
    }

    /// <summary>
    /// 运行期按预算收一次：在界面上按运行期上限（且只在跟底时），不在界面上直接按后台上限。
    /// 调用方必须先把流式条目的来源回填好——锚点就是那些来源消息
    /// </summary>
    public void TrimToBudget()
    {
        if (Window.IsDetached) return; //裁剪按「窗口跟着末尾」算锚点,脱离时不动
        bool trimmed = _isDisplayed() ? _trimmer.TrimIfNeeded() : _trimmer.TrimToBackgroundBudget();
        if (trimmed) SyncFlags();
    }

    /// <summary>
    /// 切走之后把条目压到后台上限。
    ///
    /// 排到队尾而不是就地做：切走那一刻视图还绑在本实例上（页面壳先翻显示状态，
    /// DataContext 的替换晚一步到），就地裁等于在「让切换变快」这件事上先付一次布局；
    /// 排到队尾时视图已经换给新会话，本集合不再有人绑，裁剪是纯内存操作。
    /// </summary>
    public void ScheduleBackgroundTrim()
    {
        // 两个数分开量:排队延迟说明这次裁剪有没有被饿着,耗时说明它值不值得占关键路径
        long queuedAt = StartupPhaseProbe.Begin();
        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisplayed()) return; //这一小会儿里又切回来了,当前上限自己会管
            if (Window.IsDetached) return; //停在搜索跳到的那一段:切回来还在原处,本来就只画了一小段

            StartupPhaseProbe.End("conversation/trim-delay", queuedAt);
            long trimBegin = StartupPhaseProbe.Begin();
            int before = _items.Count;
            if (_trimmer.TrimToBackgroundBudget()) SyncFlags();
            StartupPhaseProbe.End($"conversation/trim:{before}->{_items.Count}", trimBegin);
            // Normal 而不是 Background:切走的会话往往正在流式输出,而流式期间高优先级任务
            // 不断进来,Background 会被饿着——那等于切回去时这次裁剪还没发生。
            // Normal 同样排在 DataContext 替换之后(替换是同步做完的),不会误裁到已经绑上的集合
        }, DispatcherPriority.Normal);
    }

    /// <summary>换会话前清掉窗口与两个状态位</summary>
    public void Reset()
    {
        Window.Clear();
        HasLoadedEarlier = false;
        SyncFlags();
    }

    private void Prepend(IReadOnlyList<ChatMessage> history, (int From, int To) range)
    {
        _renderer.Prepend(history, range);
        SyncFlags();
    }

    /// <summary>窗口被别处改过（对账重放挪了起点）：把状态位重新投影出来</summary>
    public void NotifyWindowChanged() => SyncFlags();

    // 窗口变了之后把状态位投影出来
    private void SyncFlags()
    {
        HasEarlierMessages = Window.HasEarlier;
        HasLaterMessages = Window.IsDetached;
    }
}
