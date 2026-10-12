using System.Collections.Generic;
using System.Linq;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 把一批路径按目录聚合成不超过 <see cref="MaxGroups"/> 组，用于命中范围过宽时的分布展示。
///
/// 做法是<b>自顶向下逐级展开</b>：先把全部命中归到最高的公共目录，再反复挑最重的组拆成子目录，
/// 拆完组数仍 ≤ 上限才拆。于是密集处拆得细、稀疏处停在高层，且各组<b>互不重叠</b>，
/// 每组数值就是那一片的真实合计、总和等于全部权重。组的标签有三种：
/// <list type="bullet">
/// <item><c>src/A</c>：整棵子树</item>
/// <item><c>src/A/*</c>：src/A 的直属文件（同时列出了它的子目录时才单列）</item>
/// <item><c>src/A（其余）</c>：子目录多到放不下时，挑出明显偏重的子目录后剩下的部分</item>
/// </list>
/// 只有一个子目录、自身无文件的中间层会被跳过（src → src/UiharuMind.Core 直接显示后者）。
///
/// Glob（每条目权重 1）与 Grep（每文件权重 = 命中数）共用。
/// </summary>
internal static class PathGrouping
{
    /// <summary>分组上限</summary>
    internal const int MaxGroups = 50;

    /// <summary>一组聚合结果：标签（见类注释）与该组的权重合计</summary>
    internal readonly record struct PathGroup(string Prefix, int Total);

    private enum SlotKind
    {
        Subtree,
        Direct,
        Rest,
    }

    private readonly record struct Slot(DirNode Node, SlotKind Kind, int Weight);

    private sealed class DirNode(string path)
    {
        public readonly string Path = path;
        public readonly Dictionary<string, DirNode> Children = new(StringComparer.Ordinal);
        public int DirectWeight;
        public int SubtreeWeight;
    }

    /// <summary>
    /// 聚合，组数保证 ≤ <see cref="MaxGroups"/>（根目录数超过上限这种极端情况除外）。
    /// </summary>
    /// <param name="items">路径与权重（Grep 权重 = 该文件命中数；Glob 权重 = 1）</param>
    /// <returns>聚合组，按权重降序再按标签字典序</returns>
    internal static List<PathGroup> Group(IReadOnlyList<(string Path, int Weight)> items)
    {
        if (items.Count == 0) return [];

        DirNode top = BuildTree(items);
        List<Slot> slots = Expand(top);
        var frozen = new HashSet<DirNode>(); // 展开放不下、也挑不出偏重子目录的节点，不再尝试

        while (true)
        {
            List<Slot> candidates = slots
                .Where(s => s.Kind == SlotKind.Subtree && s.Node.Children.Count > 0 && !frozen.Contains(s.Node))
                .ToList();
            if (candidates.Count == 0) break;

            // 同权重的一批一起展开：等重的兄弟要么都拆要么都不拆，不出现「拆了一半」的不对称视图
            int heaviest = candidates.Max(s => s.Weight);
            List<Slot> batch = candidates.Where(s => s.Weight == heaviest).ToList();
            List<List<Slot>> expansions = batch.Select(s => Expand(s.Node)).ToList();
            int growth = expansions.Sum(parts => parts.Count - 1);

            if (slots.Count + growth <= MaxGroups)
            {
                for (int i = 0; i < batch.Count; i++) Replace(slots, batch[i], expansions[i]);
                continue;
            }

            foreach (Slot slot in batch)
            {
                frozen.Add(slot.Node);
                List<Slot>? partial = ExpandHeavyOnly(slot.Node, MaxGroups - slots.Count + 1);
                if (partial != null) Replace(slots, slot, partial);
            }
        }

        return slots
            .Select(s => new PathGroup(Label(s), s.Weight))
            .OrderByDescending(g => g.Total)
            .ThenBy(g => g.Prefix, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>建目录树，返回虚拟顶点（其子节点是各路径根："."、"/"、"C:" 等）</summary>
    private static DirNode BuildTree(IReadOnlyList<(string Path, int Weight)> items)
    {
        var top = new DirNode("");
        var chain = new List<string>();
        foreach ((string path, int weight) in items)
        {
            chain.Clear();
            string dir = ParentOrSelf(path);
            while (true)
            {
                chain.Add(dir);
                string next = ParentOrSelf(dir);
                if (next == dir) break; // 根都满足父==自身："."、"/"、Windows 盘符 "C:"
                dir = next;
            }

            DirNode node = top;
            node.SubtreeWeight += weight;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (!node.Children.TryGetValue(chain[i], out DirNode? child))
                {
                    child = new DirNode(chain[i]);
                    node.Children.Add(chain[i], child);
                }

                node = child;
                node.SubtreeWeight += weight;
            }

            node.DirectWeight += weight;
        }

        return top;
    }

    /// <summary>完整展开：每个子目录一组，有直属文件再加一组</summary>
    private static List<Slot> Expand(DirNode node)
    {
        List<Slot> parts = node.Children.Values
            .Select(child => SkipPassThrough(child))
            .Select(child => new Slot(child, SlotKind.Subtree, child.SubtreeWeight))
            .ToList();
        if (node.DirectWeight > 0) parts.Add(new Slot(node, SlotKind.Direct, node.DirectWeight));
        return parts;
    }

    /// <summary>
    /// 子目录太多、完整展开放不下时：只挑出高于平均的子目录，其余（含直属文件）并成「其余」一组。
    /// 均匀分布时挑不出来，返回 null——此时整组保留比拆出一堆 1 更有用。
    /// </summary>
    private static List<Slot>? ExpandHeavyOnly(DirNode node, int budget)
    {
        if (budget < 2) return null;

        List<Slot> parts = Expand(node);
        double average = (double)node.SubtreeWeight / parts.Count;
        List<Slot> heavy = parts
            .Where(s => s.Kind == SlotKind.Subtree && s.Weight > average)
            .OrderByDescending(s => s.Weight)
            .Take(budget - 1)
            .ToList();
        if (heavy.Count == 0) return null;

        heavy.Add(new Slot(node, SlotKind.Rest, node.SubtreeWeight - heavy.Sum(s => s.Weight)));
        return heavy;
    }

    private static DirNode SkipPassThrough(DirNode node)
    {
        while (node.DirectWeight == 0 && node.Children.Count == 1)
        {
            node = node.Children.Values.First();
        }

        return node;
    }

    private static void Replace(List<Slot> slots, Slot old, List<Slot> parts)
    {
        slots.Remove(old);
        slots.AddRange(parts);
    }

    private static string Label(Slot slot) => slot.Kind switch
    {
        SlotKind.Direct => slot.Node.Path == "/" ? "/*" : slot.Node.Path + "/*",
        SlotKind.Rest => slot.Node.Path + "（其余）",
        _ => slot.Node.Path,
    };

    /// <summary>
    /// 路径的父目录。没有斜杠：Windows 盘符（C:）是路径根、父仍是它，
    /// 其余无斜杠的是相对路径根 "."；Unix 绝对路径的根是 "/"。
    /// </summary>
    private static string ParentOrSelf(string path)
    {
        int slash = path.LastIndexOf('/');
        if (slash < 0)
        {
            return path.Length == 2 && path[1] == ':' ? path : ".";
        }

        if (slash == 0) return "/";
        return path[..slash];
    }
}
