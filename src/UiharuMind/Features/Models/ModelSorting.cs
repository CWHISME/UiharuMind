using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UiharuMind.Core.AI.Core;

namespace UiharuMind.Features.Models;

/// <summary>
/// 模型列表的统一排序口径（模型页与模型下拉快照共用）。
///
/// 顺序：<b>运行中/加载中置顶 → 远程区在本地区之前 → 区内收藏在前 → 名称</b>。
/// 远程与本地各自成块、不交叉混排，收藏只在所属分区内置顶；运行中/加载中的模型是
/// 当前正在用的，单独提到最前，不受分区限制。
/// </summary>
public static class ModelSorting
{
    /// <summary>
    /// 模型名的比较器：大小写无关的序数比较（模型名是标识符，不该随界面语言漂移）。
    /// </summary>
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    private static readonly IComparer<ModelRunningData> ModelComparer =
        Comparer<ModelRunningData>.Create(Compare);

    /// <summary>
    /// 比较两个模型
    /// </summary>
    /// <param name="x">模型</param>
    /// <param name="y">模型</param>
    /// <returns>比较结果</returns>
    public static int Compare(ModelRunningData x, ModelRunningData y)
    {
        int byActive = (x.IsRunning || x.IsLoading ? 0 : 1).CompareTo(y.IsRunning || y.IsLoading ? 0 : 1);
        if (byActive != 0) return byActive;

        int byRemote = (x.IsRemoteModel ? 0 : 1).CompareTo(y.IsRemoteModel ? 0 : 1);
        if (byRemote != 0) return byRemote;

        int byFavorite = (x.IsFavorite ? 0 : 1).CompareTo(y.IsFavorite ? 0 : 1);
        if (byFavorite != 0) return byFavorite;

        return NameComparer.Compare(x.ModelName, y.ModelName);
    }

    /// <summary>
    /// 就地把集合排成 <see cref="Compare"/> 的顺序。
    ///
    /// 刻意不逐次相邻换位：那会把「一个模型换名次」摊成其移动距离那么多次
    /// <see cref="ObservableCollection{T}.Move"/>（每次都是一条 CollectionChanged，
    /// 条数多时会把 ListBox 刷一片）。这里先取稳定排序的目标序列，再按位置直接用
    /// Move 把该就位的元素搬过来——单个名次变化只产生 1 次集合变更，整体变化也不
    /// 超过 n 次；已经有序时零变更。稳定排序保证同键元素不产生虚假移动。
    /// </summary>
    /// <param name="items">模型列表</param>
    public static void Sort(ObservableCollection<ModelRunningData> items)
    {
        if (items.Count < 2) return;

        List<ModelRunningData> ordered = items.OrderBy(x => x, ModelComparer).ToList();
        for (int k = 0; k < ordered.Count; k++)
        {
            if (ReferenceEquals(items[k], ordered[k])) continue;
            int from = k + 1;
            while (!ReferenceEquals(items[from], ordered[k])) from++;
            items.Move(from, k);
        }
    }
}
