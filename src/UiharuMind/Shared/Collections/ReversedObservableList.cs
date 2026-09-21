/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace UiharuMind.Shared.Collections;

/// <summary>
/// 追加序集合的<b>倒序只读视图</b>：底层只往尾部 <c>Add</c>，对外看到的第 0 项是最新那条。
///
/// 存在的理由是性能：直接用 <c>ObservableCollection.Insert(0, item)</c> 会真搬动底层数组里
/// 每一个引用（10 万条就是每来一条日志搬 800KB）；这里对外发的 <c>Insert(0)</c> 只是
/// <b>通知语义</b>上「旧条目的可见序号 +1」，虚拟化面板据此把已有容器整体下移一格，
/// 没有任何数组拷贝。
/// </summary>
/// <typeparam name="T">元素类型</typeparam>
public class ReversedObservableList<T> : IReadOnlyList<T>, IList, INotifyCollectionChanged
{
    private readonly List<T> _items = new();

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int Count => _items.Count;

    /// <summary>倒序取值：第 0 项是最新追加的那条</summary>
    public T this[int index] => _items[_items.Count - 1 - index];

    /// <summary>
    /// 追加一条，对外表现为插入到顶部
    /// </summary>
    /// <param name="item">新条目</param>
    public void Append(T item)
    {
        _items.Add(item);
        CollectionChanged?.Invoke(this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, 0));
    }

    /// <summary>
    /// 裁掉最早的若干条，对外表现为从<b>末尾</b>移除
    /// </summary>
    /// <param name="count">裁掉的条数</param>
    public void TrimOldest(int count)
    {
        if (count <= 0 || count > _items.Count) return;
        _items.RemoveRange(0, count);
        // 末尾成批移除,用 Reset 而不是逐条发:批量场景下逐条通知比重建还贵
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// 整体换成另一批（切换筛选条件时用）
    /// </summary>
    /// <param name="items">新的一批，仍按追加序给入</param>
    public void Reset(IEnumerable<T> items)
    {
        _items.Clear();
        _items.AddRange(items);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>按追加序枚举的副本，导出与统计用</summary>
    /// <returns>独立副本</returns>
    public List<T> ToAppendOrderList() => new(_items);

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = _items.Count - 1; i >= 0; i--) yield return _items[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // IList 只为满足虚拟化面板的随机访问要求,写入一律不支持
    bool IList.IsFixedSize => false;
    bool IList.IsReadOnly => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;
    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    int IList.IndexOf(object? value)
    {
        int index = value is T item ? _items.LastIndexOf(item) : -1;
        return index < 0 ? -1 : _items.Count - 1 - index;
    }

    bool IList.Contains(object? value) => value is T item && _items.Contains(item);
    void ICollection.CopyTo(Array array, int index)
    {
        for (int i = 0; i < Count; i++) array.SetValue(this[i], index + i);
    }

    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();
}
