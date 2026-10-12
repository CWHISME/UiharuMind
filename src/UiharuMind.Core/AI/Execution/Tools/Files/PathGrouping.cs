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

    // 目录树顺序：逐段按 Ordinal 比较，前缀短的在前（见 TreeOrderKey）
    private static readonly Comparer<string[]> TreeOrder = Comparer<string[]>.Create((a, b) =>
    {
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            int cmp = string.CompareOrdinal(a[i], b[i]);
            if (cmp != 0) return cmp;
        }

        return a.Length.CompareTo(b.Length);
    });

    /// <summary>组的种类，决定标签形状（见类注释）</summary>
    internal enum EGroupKind
    {
        /// <summary>整棵子树，标签 <c>src/A</c></summary>
        Subtree,

        /// <summary>目录的直属文件（它的子目录另有组列出），标签 <c>src/A/*</c></summary>
        Direct,

        /// <summary>
        /// 子目录多到完整展开会超上限时，只单列高于平均的子目录，剩下的子目录连同直属文件
        /// 并成这一组，标签 <c>src/A（其余）</c>。数值是「src/A 减去已单列部分」，不是整个 src/A
        /// </summary>
        Rest,
    }

    /// <summary>一组聚合结果：标签、权重合计、路径条数（Grep 即文件数）与种类</summary>
    internal readonly record struct PathGroup(string Prefix, int Total, int Count, EGroupKind Kind);

    private readonly record struct Slot(DirNode Node, EGroupKind Kind, int Weight, int Count);

    private sealed class DirNode(string path)
    {
        public readonly string Path = path;
        public readonly Dictionary<string, DirNode> Children = new(StringComparer.Ordinal);
        public int DirectWeight;
        public int SubtreeWeight;
        public int DirectCount;
        public int SubtreeCount;
    }

    /// <summary>
    /// 聚合，组数保证 ≤ <see cref="MaxGroups"/>（根目录数超过上限这种极端情况除外）。
    /// </summary>
    /// <param name="items">路径与权重（Grep 权重 = 该文件命中数；Glob 权重 = 1）</param>
    /// <returns>聚合组，按目录树顺序（见 <see cref="TreeOrderKey"/>）</returns>
    internal static List<PathGroup> Group(IReadOnlyList<(string Path, int Weight)> items)
    {
        if (items.Count == 0) return [];

        DirNode top = BuildTree(items);
        List<Slot> slots = Expand(top);
        var frozen = new HashSet<DirNode>(); // 展开放不下、也挑不出偏重子目录的节点，不再尝试

        while (true)
        {
            List<Slot> candidates = slots
                .Where(s => s.Kind == EGroupKind.Subtree && s.Node.Children.Count > 0 && !frozen.Contains(s.Node))
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
            .OrderBy(TreeOrderKey, TreeOrder)
            .Select(s => new PathGroup(Label(s), s.Weight, s.Count, s.Kind))
            .ToList();
    }

    /// <summary>
    /// 按目录树排，而不按权重：「其余」只有挨着被单列的兄弟才看得懂是相对谁而言，
    /// 按权重排会把它们拆散。同一目录内：直属文件在前、子目录按名、「其余」在后
    /// </summary>
    private static string[] TreeOrderKey(Slot slot)
    {
        string path = slot.Node.Path;
        string[] segments = path switch
        {
            "." => [],
            "/" => [""],
            _ => path.Split('/'),
        };

        return slot.Kind switch
        {
            EGroupKind.Direct => [.. segments, "\0"],
            EGroupKind.Rest => [.. segments, "\uFFFF"],
            _ => segments,
        };
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
            node.SubtreeCount++;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (!node.Children.TryGetValue(chain[i], out DirNode? child))
                {
                    child = new DirNode(chain[i]);
                    node.Children.Add(chain[i], child);
                }

                node = child;
                node.SubtreeWeight += weight;
                node.SubtreeCount++;
            }

            node.DirectWeight += weight;
            node.DirectCount++;
        }

        return top;
    }

    /// <summary>完整展开：每个子目录一组，有直属文件再加一组</summary>
    private static List<Slot> Expand(DirNode node)
    {
        List<Slot> parts = node.Children.Values
            .Select(child => SkipPassThrough(child))
            .Select(child => new Slot(child, EGroupKind.Subtree, child.SubtreeWeight, child.SubtreeCount))
            .ToList();
        if (node.DirectCount > 0) parts.Add(new Slot(node, EGroupKind.Direct, node.DirectWeight, node.DirectCount));
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
            .Where(s => s.Kind == EGroupKind.Subtree && s.Weight > average)
            .OrderByDescending(s => s.Weight)
            .Take(budget - 1)
            .ToList();
        if (heavy.Count == 0) return null;

        heavy.Add(new Slot(node, EGroupKind.Rest,
            node.SubtreeWeight - heavy.Sum(s => s.Weight), node.SubtreeCount - heavy.Sum(s => s.Count)));
        return heavy;
    }

    /// <summary>
    /// 标签图例：只解释本次结果里实际出现的特殊标签，没有就返回 null
    /// </summary>
    /// <param name="groups">聚合结果</param>
    /// <returns>一句说明，或 null</returns>
    internal static string? DescribeLabels(IReadOnlyList<PathGroup> groups)
    {
        List<string> parts = [];
        if (groups.Any(g => g.Kind == EGroupKind.Direct)) parts.Add("「目录/*」是该目录的直属文件");
        if (groups.Any(g => g.Kind == EGroupKind.Rest)) parts.Add("「目录（其余）」是该目录下未单列出来的部分");
        return parts.Count == 0 ? null : string.Join("，", parts) + "。";
    }

    private static DirNode SkipPassThrough(DirNode node)
    {
        while (node.DirectCount == 0 && node.Children.Count == 1)
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
        EGroupKind.Direct => slot.Node.Path == "/" ? "/*" : slot.Node.Path + "/*",
        EGroupKind.Rest => slot.Node.Path + "（其余）",
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
