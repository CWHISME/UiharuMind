/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 会话历史：一个<b>跨线程安全</b>的 <see cref="ChatMessage"/> 列表。
///
/// <para>为什么不是裸 <c>List&lt;ChatMessage&gt;</c>：写方在后台线程（执行轮的
/// <c>SessionChatHistoryProvider</c> 落盘、子代理交回报告、群成员投递），
/// 读方在 UI 线程（对账、渲染、命中检索、token 统计）。两边共用同一份
/// <c>List</c> 时，读方一次整表枚举撞上写方的 <c>Add</c> 就是
/// <c>Collection was modified; enumeration operation may not execute</c>；
/// 按下标读的地方则退化成 <c>ArgumentOutOfRange</c> 或静默少几条。</para>
///
/// <para>用法约定：<b>写方</b>照旧调 <c>Add/RemoveAt/...</c>（每次调用各自持锁，
/// 临界区里只有一次 <c>List</c> 操作，不做 IO、不回调）；<b>读方要跨多条操作</b>
/// （按下标切片、整表枚举、算命中）必须先取 <see cref="Snapshot"/>，
/// 在快照上做完——拿活引用再慢慢枚举，锁帮不了你。</para>
///
/// <para>⚠️ 实现 <see cref="IList{T}"/> 是有代价的：LINQ 见到 <c>IList</c> 会走
/// <c>Skip/Take/ElementAt/Last</c> 的快速路径，直接读活 <see cref="Count"/> 与索引器，
/// <b>绕过</b> <see cref="GetEnumerator"/> 那层快照。单次读各自带锁不会抛，但一串 LINQ
/// 之间历史仍可能在变。要一致就先 <see cref="Snapshot"/>，再在数组上用 LINQ。</para>
/// </summary>
public sealed class SessionHistory : IList<ChatMessage>, IReadOnlyList<ChatMessage>
{
    private readonly object _gate = new();
    private readonly List<ChatMessage> _items;

    /// <summary>空历史</summary>
    public SessionHistory() => _items = [];

    /// <summary>用一份现成的列表做初值（拷贝一份，不与入参共享）</summary>
    /// <param name="items">初值</param>
    public SessionHistory(IEnumerable<ChatMessage> items) => _items = [..items];

    /// <summary>条数</summary>
    public int Count
    {
        get
        {
            lock (_gate) return _items.Count;
        }
    }

    /// <summary>按下标取一条</summary>
    /// <param name="index">下标</param>
    public ChatMessage this[int index]
    {
        get
        {
            lock (_gate) return _items[index];
        }
        set
        {
            lock (_gate) _items[index] = value;
        }
    }

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <summary>取第一条，空历史返回 null。与 <c>Count &gt; 0 ? History[0] : null</c> 的区别是不给并发留缝</summary>
    public ChatMessage? Head
    {
        get
        {
            lock (_gate) return _items.Count > 0 ? _items[0] : null;
        }
    }

    /// <summary>取最后一条，空历史返回 null</summary>
    public ChatMessage? Tail
    {
        get
        {
            lock (_gate) return _items.Count > 0 ? _items[^1] : null;
        }
    }

    /// <summary>
    /// 取一份<b>定格快照</b>。锁内只做一次 <c>ToArray</c>，出锁之后随便怎么枚举都不会再变。
    /// 跨多条操作的读一律走这里。
    /// </summary>
    /// <returns>此刻的历史副本</returns>
    public ChatMessage[] Snapshot()
    {
        lock (_gate) return _items.ToArray();
    }

    /// <summary>
    /// 只取 <paramref name="fromIndex"/> 起的那一段快照。落盘、补画尾巴这类"只关心新增的那几条"的
    /// 读走这里——为拿 2 条消息复制整份历史（万条会话约 80 KB）是白付的 gen0 垃圾。
    /// 下标是<b>绝对</b>的，与 <see cref="Snapshot"/> 同一套：负数按 0 处理（返回整份），
    /// 不小于 <see cref="Count"/> 时返回空。
    /// </summary>
    /// <param name="fromIndex">起始下标</param>
    /// <returns>此刻从该下标到末尾的副本</returns>
    public ChatMessage[] SnapshotFrom(int fromIndex)
    {
        lock (_gate)
        {
            int start = Math.Max(0, fromIndex);
            if (start >= _items.Count) return [];

            ChatMessage[] slice = new ChatMessage[_items.Count - start];
            _items.CopyTo(start, slice, 0, slice.Length);
            return slice;
        }
    }

    /// <inheritdoc />
    public void Add(ChatMessage item)
    {
        lock (_gate) _items.Add(item);
    }

    /// <summary>
    /// 追加一条并返回它落在的下标。与「先读 <see cref="Count"/> 再 <see cref="Add"/>」的区别：
    /// 后者在并发追加下会错位——两句话之间别人插了一条，记下的下标就指着别人的消息，
    /// 落盘时会把别人的那条再写一遍。
    /// </summary>
    /// <param name="item">要追加的消息</param>
    /// <returns>它此刻的下标</returns>
    public int AddReturningIndex(ChatMessage item)
    {
        lock (_gate)
        {
            _items.Add(item);
            return _items.Count - 1;
        }
    }

    /// <summary>批量追加</summary>
    /// <param name="items">要追加的消息</param>
    public void AddRange(IEnumerable<ChatMessage> items)
    {
        lock (_gate) _items.AddRange(items);
    }

    /// <inheritdoc />
    public void Insert(int index, ChatMessage item)
    {
        lock (_gate) _items.Insert(index, item);
    }

    /// <inheritdoc />
    public bool Remove(ChatMessage item)
    {
        lock (_gate) return _items.Remove(item);
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        lock (_gate) _items.RemoveAt(index);
    }

    /// <summary>按下标区间删除（压缩历史时用）</summary>
    /// <param name="index">起始下标</param>
    /// <param name="count">条数</param>
    public void RemoveRange(int index, int count)
    {
        lock (_gate) _items.RemoveRange(index, count);
    }

    /// <summary>按条件删除，返回删掉的条数。<b>谓词在锁内执行</b>，里面不许再碰会话（调 Save 之类会成环）</summary>
    /// <param name="match">条件</param>
    public int RemoveAll(Predicate<ChatMessage> match)
    {
        lock (_gate) return _items.RemoveAll(match);
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate) _items.Clear();
    }

    /// <inheritdoc />
    public bool Contains(ChatMessage item)
    {
        lock (_gate) return _items.Contains(item);
    }

    /// <inheritdoc />
    public int IndexOf(ChatMessage item)
    {
        lock (_gate) return _items.IndexOf(item);
    }

    /// <inheritdoc />
    public void CopyTo(ChatMessage[] array, int arrayIndex)
    {
        lock (_gate) _items.CopyTo(array, arrayIndex);
    }

    /// <summary>
    /// 枚举<b>快照</b>，不是活列表。这样 <c>foreach</c> 不会因为别处的追加而抛
    /// <c>Collection was modified</c>，代价是每次 <c>foreach</c> 复制一次引用数组——
    /// 热路径（每 token）请自己 <see cref="Snapshot"/> 一次重复用，别反复 <c>foreach</c>。
    /// </summary>
    public IEnumerator<ChatMessage> GetEnumerator() =>
        ((IEnumerable<ChatMessage>)Snapshot()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
