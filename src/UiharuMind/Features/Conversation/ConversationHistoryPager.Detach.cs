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
using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 开窗的「脱离末尾」那一半：搜索跳到旧消息时截断重载、往后续、回到最新，
/// 以及脱离期间历史被追加或删改时窗口怎么跟（见 ADR 0056）。
///
/// 规矩只有一条：<b>动历史的人都经过这里</b>。脱离期间末尾没画，追加就先回到最新；
/// 删改就按下标修正窗口——窗口记的是下标，不修正的话往前往后续都会跳过同样多条
/// </summary>
public sealed partial class ConversationHistoryPager
{
    private const int JumpBatchSize = 40; //分批往前续到命中时每批多少条:比滚到顶的一窗大,又小到一批布局不卡顿
    private const int LaterBatchSize = 20; //脱离末尾时往下滚到底每批续多少条:是滚动途中的一下停顿,宁可多续几次
    private const int JumpContextBefore = 10; //截断重载时命中之前留几条
    // 命中之后留几条。落地那一下的耗时几乎全在新画的这批条目的首次布局上(按条数涨),
    // 30 条约 68 个条目、15 条约 40 个,实测快 25～70%;再往下读由滚到底续批接上
    private const int JumpContextAfter = 15;

    /// <summary>从旧消息那段回到了最新（条目整窗换成了末尾首屏，视图据此贴回底部）</summary>
    public event Action? ReturnedToLatest;

    /// <summary>目标消息此刻是否在渲染窗口里（在就不必重载，直接滚过去）</summary>
    /// <param name="index">完整历史里的下标</param>
    /// <returns>在窗内为 true</returns>
    public bool IsInWindow(int index) => index >= Window.Start && index < Window.EndFor(_historySource().Count);

    /// <summary>
    /// 截断重载：清掉现有条目，只围着第 <paramref name="index"/> 条重画前后一小段（搜索跳到旧消息）。
    ///
    /// 不管跳多远都只画这一小段——往前一批批续到那里，几百条会一帧帧前插，界面跟着闪；
    /// 代价是窗口脱离历史末尾，之后的消息要往下滚才续出来（<see cref="LoadLater"/>）。
    /// 只能在<b>没人跑</b>时调：有轮在跑时实时流在往条目末尾追加，截掉末尾就接不上了
    /// </summary>
    /// <param name="index">目标消息在完整历史里的下标</param>
    /// <returns>重画了返回 true；越界返回 false</returns>
    public bool JumpTo(int index)
    {
        IReadOnlyList<ChatMessage> history = _historySource();
        if (index < 0 || index >= history.Count) return false;

        DiscardItems();
        (int from, int to) = Window.Detach(index - JumpContextBefore, index + JumpContextAfter, history.Count);
        _renderer.Append(history, from, to, liveTail: false);
        HasLoadedEarlier = true;
        SyncFlags();
        return true;
    }

    /// <summary>
    /// 往前续一批，直到第 <paramref name="index"/> 条进窗（有轮在跑、截不得时的搜索跳转）。每次只续一批，
    /// 由调用方分批调、批间让出 UI 线程——跳到几百条之前时一次性前插会把界面冻住好几秒
    /// </summary>
    /// <param name="index">目标消息在完整历史里的下标</param>
    /// <returns>真的前插了条目返回 true；目标已在窗内（或越界）返回 false</returns>
    public bool LoadEarlierToward(int index)
    {
        IReadOnlyList<ChatMessage> history = _historySource();
        if (index < 0 || index >= history.Count || index >= Window.Start) return false;
        if (Window.Extend(history.Count, JumpBatchSize) is not { } range) return false;

        Prepend(history, range);
        HasLoadedEarlier = true;
        return true;
    }

    /// <summary>脱离末尾时往后续一批（滚到底自动调）。追加在下面，视口不用补偿</summary>
    /// <returns>真的追加了条目返回 true</returns>
    public bool LoadLater()
    {
        IReadOnlyList<ChatMessage> history = _historySource();
        (int From, int To)? range = Window.ExtendLater(history.Count, LaterBatchSize);
        SyncFlags();
        if (range is not { } later) return false;

        _renderer.Append(history, later.From, later.To, liveTail: false);
        return true;
    }

    /// <summary>脱离末尾时回到最新：按切会话那样从末尾首屏重放。没脱离时什么都不做</summary>
    /// <returns>真的回去了返回 true</returns>
    public bool ReturnToLatest()
    {
        if (!Window.IsDetached) return false;

        DiscardItems();
        HasLoadedEarlier = false;
        Replay(_historySource(), _isTurnRunning());
        ReturnedToLatest?.Invoke();
        return true;
    }

    /// <summary>
    /// 历史末尾追加了消息（落盘、交回报告）。脱离末尾时不往窗口里追加——末尾没画，追加上去就隔着一段空当——
    /// 直接回到最新，新的那条随重放画出来
    /// </summary>
    /// <param name="history">当前历史</param>
    /// <param name="fromIndex">新增段的起始下标</param>
    /// <param name="ownTurn">自己那一轮正在跑</param>
    /// <param name="streaming">别处驱动的那一轮正往这里流内容</param>
    public void AppendPersisted(IReadOnlyList<ChatMessage> history, int fromIndex, bool ownTurn, bool streaming)
    {
        if (ReturnToLatest()) return;
        _renderer.AppendPersisted(history, fromIndex, ownTurn, streaming);
    }

    /// <summary>
    /// 历史里删掉了几条（删除消息、重试截断），在删除<b>之后</b>调。窗口按下标前移，
    /// 截到窗内某条及之后的全部时窗口自然接回末尾——重试接着往末尾追加，不必整窗重放
    /// </summary>
    /// <param name="removedIndices">被删消息删除之前的下标</param>
    public void NoteRemoved(IReadOnlyCollection<int> removedIndices)
    {
        if (removedIndices.Count == 0) return;
        Window.NoteRemoved(removedIndices, _historySource().Count);
        SyncFlags();
    }

    // 截断重载与回到最新都是整窗换掉:条目里有本会话现解出来的大位图,先摘绑定再释放
    private void DiscardItems()
    {
        _items.DiscardAll();
    }
}
