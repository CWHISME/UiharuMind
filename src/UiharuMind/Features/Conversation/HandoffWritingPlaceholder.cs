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
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 正在整理交接文档的会话内占位卡（整理是多一次模型请求，会话流里不能毫无动静）
/// </summary>
public sealed class HandoffWritingPlaceholder
{
    private readonly IList<ConversationItemBase> _items;
    private readonly Action _scrollToEnd;
    private HandoffWritingItem? _item;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="items">会话条目集合</param>
    /// <param name="scrollToEnd">挂上或收掉之后让会话流跟底</param>
    public HandoffWritingPlaceholder(IList<ConversationItemBase> items, Action scrollToEnd)
    {
        _items = items;
        _scrollToEnd = scrollToEnd;
    }

    /// <summary>挂上占位卡。事件是串行的，同一次整理不会重复挂</summary>
    public void Show()
    {
        if (_item != null) return;
        HandoffWritingItem item = new() { Message = Loc.Text(LangKey.HandoffWriting) };
        _item = item;
        item.BeginLiveTiming(); //首值等节拍：建卡可能不在 UI 线程，赋值留给节拍回调
        _items.Add(item);
        _scrollToEnd();
    }

    /// <summary>收掉占位卡（整理成功、失败或无可整理）</summary>
    public void Hide()
    {
        if (_item is not { } item) return;
        _item = null;
        item.StopTiming();
        if (_items.Remove(item)) _scrollToEnd();
    }

    /// <summary>条目已被整体清掉时只丢引用并停表；若整理还在跑，切回会话时由装载重新挂上</summary>
    public void Forget()
    {
        if (_item is not { } item) return;
        _item = null;
        item.StopTiming(); //集合已清，泵里的注册还在——不摘它会每拍自续、全局泵停不下来
    }
}
