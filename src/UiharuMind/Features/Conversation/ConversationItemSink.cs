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
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 会话条目集合的<b>唯一写入口</b>：所有往 <see cref="Items"/> 里加条目的地方都走这里，
/// 判重按条目的稳定身份（<see cref="ConversationItemBase.Identity"/>）而不是对象引用或显示文本。
///
/// <para>
/// 从前这个集合有十几条写入口，判重闸只装在历史补画那一侧，实时流那一侧各自靠
/// <c>CallId</c> 或引用自判。于是「同一条消息画两遍」的往复机制是：只要判重口径有一处不齐，
/// 每个消息边界都会补出重复条目（真实日志里 5 小时 298 次对账，不是异常路径）。
/// </para>
/// <para>
/// 收口之后判重只剩一条规则：<b>同一个身份只允许一个条目</b>。身份在条目创建时定下
/// （流式阶段是临时键，落盘配对时换成消息标识），所以「猜」这件事整个消失了。
/// </para>
/// <para>
/// 没有身份的条目（错误提示、占位卡、审批卡）照常加入、不参与判重——它们不对应任何历史消息，
/// 重复与否由各自的产生方负责。
/// </para>
/// </summary>
public sealed class ConversationItemSink
{
    private readonly ObservableCollection<ConversationItemBase> _items;
    private readonly Dictionary<ConversationItemIdentity, ConversationItemBase> _byIdentity =
        new(ConversationItemIdentityComparer.Instance);

    /// <param name="items">界面绑定的那一份集合</param>
    public ConversationItemSink(ObservableCollection<ConversationItemBase> items)
    {
        _items = items;
    }

    /// <summary>界面绑定的那一份集合（只读用途；写入请走本类）</summary>
    public ObservableCollection<ConversationItemBase> Items => _items;

    /// <summary>当前条目数</summary>
    public int Count => _items.Count;

    /// <summary>
    /// 加入一个条目。身份已存在则<b>不加</b>并返回 false——这就是全部判重规则
    /// </summary>
    /// <param name="item">待加入的条目</param>
    /// <returns>真的加进去了返回 true</returns>
    public bool Add(ConversationItemBase item)
    {
        if (item.Identity is { } identity && _byIdentity.ContainsKey(identity)) return false;

        _items.Add(item);
        if (item.Identity is { } added) _byIdentity[added] = item;
        return true;
    }

    /// <summary>
    /// 把一批条目加入集合，逐个按身份判重
    /// </summary>
    /// <param name="fresh">待加入的条目</param>
    /// <returns>真正加进去的条数</returns>
    public int AddRange(IEnumerable<ConversationItemBase> fresh)
    {
        int added = 0;
        foreach (ConversationItemBase item in fresh)
        {
            if (Add(item)) added++;
        }

        return added;
    }

    /// <summary>
    /// 在指定位置插入一个条目（往前续窗用）。身份已存在则不加
    /// </summary>
    /// <param name="index">插入位置</param>
    /// <param name="item">待插入的条目</param>
    /// <returns>真的插进去了返回 true</returns>
    public bool Insert(int index, ConversationItemBase item)
    {
        if (item.Identity is { } identity && _byIdentity.ContainsKey(identity)) return false;

        _items.Insert(index, item);
        if (item.Identity is { } added) _byIdentity[added] = item;
        return true;
    }

    /// <summary>
    /// 摘掉一个条目并销掉它的身份
    /// </summary>
    /// <param name="item">要摘的条目</param>
    /// <returns>真的摘掉了返回 true</returns>
    public bool Remove(ConversationItemBase item)
    {
        if (!_items.Remove(item)) return false;
        Forget(item);
        return true;
    }

    /// <summary>
    /// 摘掉指定位置的条目并销掉它的身份
    /// </summary>
    /// <param name="index">下标</param>
    public void RemoveAt(int index)
    {
        ConversationItemBase item = _items[index];
        _items.RemoveAt(index);
        Forget(item);
    }

    /// <summary>
    /// 清空集合（连同身份索引）。位图随条目走，所以走 <see cref="ConversationItemDiscard"/>
    /// </summary>
    public void DiscardAll()
    {
        _items.DiscardAll();
        _byIdentity.Clear();
    }

    /// <summary>
    /// 丢弃一段连续的条目（裁剪用），连同它们的身份登记
    /// </summary>
    /// <param name="from">起始下标</param>
    /// <param name="count">条数</param>
    public void DiscardRange(int from, int count)
    {
        for (int i = from + count - 1; i >= from; i--)
        {
            if (i < 0 || i >= _items.Count) continue;
            Forget(_items[i]);
        }

        _items.DiscardRange(from, count);
    }

    /// <summary>
    /// 丢弃指定的那些条目，并销掉它们的身份登记。
    ///
    /// <para>
    /// 删除走这里而不是集合自带的 <c>Discard</c>：知识卡等按内容建身份的条目在来源被删后，
    /// 若身份留在表里，同内容再次检索出来的卡会被 <see cref="Add"/> 静默挡掉（真消息被吞）。
    /// </para>
    /// </summary>
    /// <param name="targets">要丢弃的条目</param>
    public void Discard(IReadOnlyCollection<ConversationItemBase> targets)
    {
        foreach (ConversationItemBase item in targets) Forget(item);
        _items.Discard(targets);
    }

    /// <summary>
    /// 把条目的临时身份换成正式身份（消息落盘、标识已知时）。
    ///
    /// <para>
    /// 换完之后要重新登记：正式身份可能已经有一个条目占着（回放先画了同一条消息），
    /// 那种情况说明<b>这个条目是重复的</b>，直接摘掉——这正是从前要靠内容启发式猜的那件事。
    /// </para>
    /// </summary>
    /// <param name="item">界面上的条目</param>
    /// <param name="message">配对到的历史消息</param>
    /// <returns>条目被摘掉（它是重复的）返回 false</returns>
    public bool Rebind(ConversationItemBase item, ChatMessage message)
    {
        if (item.Identity is not { } temporary) return true;

        Forget(item);
        item.RebindIdentity(message);
        if (item.Identity is not { } formal) return true;

        if (_byIdentity.TryGetValue(formal, out ConversationItemBase? existing) &&
            !ReferenceEquals(existing, item))
        {
            // 同一条消息的同一个内容块已经有一个条目了：这个是多画的那份
            Log.Debug($"Dropped a duplicate conversation item ({formal}); the identity was already rendered.");
            _items.Remove(item);
            item.ReleaseImages();
            return false;
        }

        _byIdentity[formal] = item;
        return true;
    }

    /// <summary>
    /// 按身份查界面上已有的条目
    /// </summary>
    /// <param name="identity">身份</param>
    /// <returns>条目；没有为 null</returns>
    public ConversationItemBase? Find(ConversationItemIdentity identity) =>
        _byIdentity.GetValueOrDefault(identity);

    /// <summary>
    /// 这条消息的某个内容块是否已经画出来了
    /// </summary>
    /// <param name="message">历史消息</param>
    /// <param name="kind">条目种类</param>
    /// <param name="contentOrdinal">内容块序号</param>
    /// <returns>已画过返回 true</returns>
    public bool IsRendered(ChatMessage message, EConversationItemKind kind, int contentOrdinal = 0) =>
        _byIdentity.ContainsKey(ConversationItemIdentity.ForMessage(message, kind, contentOrdinal));

    /// <summary>
    /// 这条消息是否已经画出了<b>任何</b>条目（对账判「这条消息漏没漏画」用）
    /// </summary>
    /// <param name="message">历史消息</param>
    /// <returns>画过返回 true</returns>
    public bool IsMessageRendered(ChatMessage message)
    {
        string id = Core.AI.Chat.ChatMessageIdentity.Ensure(message);
        foreach (ConversationItemIdentity identity in _byIdentity.Keys)
        {
            if (identity.IsFormal && string.Equals(identity.MessageId, id, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>
    /// 把集合里所有条目的身份重新登记一遍（整窗重放、外部改动之后调）
    /// </summary>
    public void Reindex()
    {
        _byIdentity.Clear();
        foreach (ConversationItemBase item in _items)
        {
            if (item.Identity is { } identity) _byIdentity[identity] = item;
        }
    }

    private void Forget(ConversationItemBase item)
    {
        if (item.Identity is not { } identity) return;
        if (_byIdentity.TryGetValue(identity, out ConversationItemBase? registered) &&
            ReferenceEquals(registered, item))
        {
            _byIdentity.Remove(identity);
        }
    }
}
