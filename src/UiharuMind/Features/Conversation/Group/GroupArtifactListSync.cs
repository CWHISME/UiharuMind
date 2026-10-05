using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 把新产物清单同步进 <see cref="ObservableCollection{T}"/>：
/// 盘上内容没变的项复用旧实例，只做最小集合操作（移除 / 插入 / 移动 / 原位更新）。
/// 复用就不重建容器，重建才会带动控件树把订阅重挂一遍（实测 P2 的泄漏点：
/// 每次重建每项都新建 4 个订阅，群一忙就在产物面板里越积越多）。
/// </summary>
public static class GroupArtifactListSync
{
    /// <summary>
    /// 以 <paramref name="artifacts"/>（须已按最近修改排序）为权威，把 <paramref name="items"/> 对齐到它
    /// </summary>
    /// <param name="items">产物列表（界面绑定的那一个）</param>
    /// <param name="artifacts">新的产物清单，最近改过的在前</param>
    public static void Apply(ObservableCollection<GroupArtifactItem> items, IReadOnlyList<GroupArtifact> artifacts)
    {
        Dictionary<string, GroupArtifactItem> current = new(GroupArtifacts.PathComparer);
        foreach (GroupArtifactItem item in items) current[item.FullPath] = item;

        List<GroupArtifactItem> next = new(artifacts.Count);
        foreach (GroupArtifact artifact in artifacts)
        {
            if (current.TryGetValue(artifact.FullPath, out GroupArtifactItem? existing))
            {
                current.Remove(artifact.FullPath); //进了 next，就不再当“该删的”
                next.Add(existing);
                // record 相等对 Authors（IReadOnlyList<string>）是引用相等，Collect 每次新建列表 → 恒不相等；
                // 用展示字段的结构化比较，真正「没变」时才不通知
                if (!SameDisplay(existing.Artifact, artifact)) existing.Update(artifact);
            }
            else
            {
                next.Add(new GroupArtifactItem(artifact));
            }
        }

        foreach (GroupArtifactItem stale in current.Values) items.Remove(stale);

        for (int i = 0; i < next.Count; i++)
        {
            GroupArtifactItem want = next[i];
            if (i < items.Count)
            {
                if (ReferenceEquals(items[i], want)) continue;
                int from = items.IndexOf(want);
                if (from < 0) items.Insert(i, want);
                else if (from > i) items.Move(from, i);
                else { items.RemoveAt(from); items.Insert(i, want); } //理论不可达（已按引用对齐）；兜底不许重复占用
            }
            else
            {
                items.Add(want);
            }
        }
    }

    // 只比「展示所需字段」：Authors SequenceEqual 而不是 record 的引用相等
    private static bool SameDisplay(GroupArtifact a, GroupArtifact b) =>
        a.FullPath == b.FullPath
        && a.DisplayPath == b.DisplayPath
        && a.Source == b.Source
        && a.LastWrite == b.LastWrite
        && a.Authors.SequenceEqual(b.Authors);
}