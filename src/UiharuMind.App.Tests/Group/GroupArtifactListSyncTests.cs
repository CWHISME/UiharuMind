using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation.Group;

namespace UiharuMind.App.Tests.Group;

/// <summary>
/// 产物面板增量同步（P2）：盘上没变的项必须复用实例——重建才会重挂订阅、把泄漏壳越积越多；
/// 变化只做原位更新或最小集合操作（插入 / 移除 / 移动）。
/// </summary>
public class GroupArtifactListSyncTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 20, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly DateTimeOffset T2 = T0.AddHours(2);

    private static GroupArtifact Artifact(string name, DateTimeOffset lastWrite, params string[] authors) =>
        new(Path.GetFullPath(name), name, EGroupArtifactSource.DraftRoom, authors, lastWrite);

    [Fact]
    public void UnchangedItems_KeepSameInstances()
    {
        ObservableCollection<GroupArtifactItem> items = new();
        GroupArtifactListSync.Apply(items, new[] { Artifact("a.md", T0), Artifact("b.md", T0) });

        GroupArtifactItem a = items[0];
        GroupArtifactItem b = items[1];

        GroupArtifactListSync.Apply(items, new[] { Artifact("a.md", T0), Artifact("b.md", T0) });

        Assert.Equal(2, items.Count);
        Assert.Same(a, items[0]);
        Assert.Same(b, items[1]);
    }

    [Fact]
    public void UnchangedItems_DoNotRaisePropertyChanged()
    {
        ObservableCollection<GroupArtifactItem> items = new();
        // 带一个作者：每次调用生成独立的 string[1]，不是 Array.Empty 共享单例——
        // 这样旧版（record 对 Authors 引用相等、恒触发 Update）跑这里会失败，测试才钉得住修复
        GroupArtifactListSync.Apply(items, new[] { Artifact("a.md", T0, "A") });
        GroupArtifactItem item = items[0];

        int raised = 0;
        item.PropertyChanged += (_, _) => raised++;

        // 新实例、Authors 也是新列表，但展示字段全同 → 不应触发 Update（P2 评审点）
        GroupArtifactListSync.Apply(items, new[] { Artifact("a.md", T0, "A") });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void ChangedItem_UpdatesInPlace_OthersKept()
    {
        ObservableCollection<GroupArtifactItem> items = new();
        GroupArtifactListSync.Apply(items, new[] { Artifact("a.md", T0), Artifact("b.md", T0) });

        GroupArtifactItem a = items[0];
        GroupArtifactItem b = items[1];

        GroupArtifactListSync.Apply(items, new[] { Artifact("a.md", T1), Artifact("b.md", T0) });

        Assert.Same(a, items[0]);
        Assert.Equal(T1, items[0].Artifact.LastWrite);
        Assert.Same(b, items[1]);
    }

    [Fact]
    public void RemovedAndAdded_ReflectInCollection()
    {
        ObservableCollection<GroupArtifactItem> items = new();
        GroupArtifactListSync.Apply(items, new[] { Artifact("a.md", T0), Artifact("b.md", T0) });

        GroupArtifactItem b = items[1];

        GroupArtifactListSync.Apply(items, new[] { Artifact("c.md", T1), Artifact("b.md", T0) });

        Assert.Equal(new[] { "c.md", "b.md" }, items.Select(x => x.Name).ToArray());
        Assert.Same(b, items[1]); //留下的项仍是旧实例
    }

    [Fact]
    public void Reorder_MovesExistingInstanceInsteadOfRebuilding()
    {
        ObservableCollection<GroupArtifactItem> items = new();
        GroupArtifactListSync.Apply(items, new[] { Artifact("a.md", T1), Artifact("b.md", T0) });

        GroupArtifactItem a = items[0];
        GroupArtifactItem b = items[1];

        // b 更新到比 a 新 → 顺序翻转，但两个实例都应复用
        GroupArtifactListSync.Apply(items, new[] { Artifact("b.md", T2), Artifact("a.md", T1) });

        Assert.Same(b, items[0]);
        Assert.Same(a, items[1]);
    }
}