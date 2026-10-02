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
using UiharuMind.Core.AI.Chat.Search;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation.Search;

/// <summary>一次搜索跳转怎么让命中进窗</summary>
public enum ESearchJumpPlan
{
    /// <summary>命中已不在历史里（删了、压缩了），不跳</summary>
    NotFound,

    /// <summary>已在渲染窗口里，直接滚过去</summary>
    InWindow,

    /// <summary>截断重载过了，只画了命中附近一小段</summary>
    Reloaded,

    /// <summary>有轮在跑、截不得，由调用方分批往前续（<see cref="ConversationSearchNavigator.LoadEarlierToward"/>）</summary>
    Incremental,
}

/// <summary>
/// 搜索跳转的决策那一半：命中怎么进窗、进窗后落到哪张卡。不碰视图——滚动、闪烁、批间让出线程
/// 归 <c>ConversationSearchJump</c>，这一半因此不用界面就能测（见 ADR 0056）
/// </summary>
public sealed class ConversationSearchNavigator
{
    private readonly IReadOnlyList<ConversationItemBase> _items;
    private readonly ConversationHistoryPager _pager;
    private readonly Func<IReadOnlyList<ChatMessage>?> _history;
    private readonly Func<bool> _canReload;

    /// <param name="items">界面条目</param>
    /// <param name="pager">开窗</param>
    /// <param name="history">当前会话历史；没有会话为 null</param>
    /// <param name="canReload">此刻能不能截断重载（会话空闲、没在整理交接文档）</param>
    public ConversationSearchNavigator(IReadOnlyList<ConversationItemBase> items, ConversationHistoryPager pager,
        Func<IReadOnlyList<ChatMessage>?> history, Func<bool> canReload)
    {
        _items = items;
        _pager = pager;
        _history = history;
        _canReload = canReload;
        pager.ReturnedToLatest += () => Anchor = null;
    }

    /// <summary>
    /// 最近一次跳到的命中；回到最新后清空。切回一个停在旧消息那段的缓存实例时，视图据此滚回原处——
    /// 滚动容器是各会话共用的那一个，偏移量留不住
    /// </summary>
    public SessionSearchHit? Anchor { get; private set; }

    /// <summary>决定并执行进窗方式（重载就在这里做完）</summary>
    /// <param name="hit">命中</param>
    /// <returns>进窗方式</returns>
    public ESearchJumpPlan Plan(SessionSearchHit hit)
    {
        if (IndexOf(hit) is not { } index) return ESearchJumpPlan.NotFound;

        Anchor = hit;
        if (_pager.IsInWindow(index)) return ESearchJumpPlan.InWindow;
        return _canReload() && _pager.JumpTo(index) ? ESearchJumpPlan.Reloaded : ESearchJumpPlan.Incremental;
    }

    /// <summary>
    /// 分批往前续一批。目标取命中的前一条：工具结果没有自己的卡、并在前一条的调用卡上，
    /// 只续到结果那条就停的话，调用还在窗外、卡找不到
    /// </summary>
    /// <param name="hit">命中</param>
    /// <returns>真的前插了条目返回 true</returns>
    public bool LoadEarlierToward(SessionSearchHit hit) =>
        IndexOf(hit) is { } index && _pager.LoadEarlierToward(Math.Max(0, index - 1));

    /// <summary>命中对应的卡片（只读，不展开）</summary>
    /// <param name="hit">命中</param>
    /// <returns>卡片；还没画出来为 null</returns>
    public ConversationItemBase? Find(SessionSearchHit hit) =>
        _history() is { } history ? ConversationSearchLocator.Find(_items, history, hit) : null;

    /// <summary>命中对应的卡片，并展开命中所在的折叠卡</summary>
    /// <param name="hit">命中</param>
    /// <returns>卡片；还没画出来为 null</returns>
    public ConversationItemBase? Reveal(SessionSearchHit hit)
    {
        ConversationItemBase? item = Find(hit);
        if (item != null) ConversationSearchLocator.Reveal(item, hit.Kind);
        return item;
    }

    private int? IndexOf(SessionSearchHit hit)
    {
        if (_history() is not { } history) return null;
        int index = ConversationSearchLocator.IndexOf(history, hit);
        return index < 0 ? null : index;
    }
}
