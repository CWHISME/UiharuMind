/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 在快照里找东西：按看得见的文字找节点（给没编号的元素兜底），按关键字挑行（省得整页重读）
/// </summary>
public static class AxSnapshotSearch
{
    /// <summary>
    /// 按看得见的文字找节点，给没有编号的元素（div 做的按钮）兜底：先整句相等、再包含，
    /// 同等匹配里可操作的元素优先；树根（名字是页面标题）不算
    /// </summary>
    /// <param name="root">最近一份快照的树</param>
    /// <param name="text">要找的文字</param>
    /// <returns>命中的节点；没有为 null</returns>
    public static AxNode? FindByText(AxNode root, string text)
    {
        string wanted = AxRoles.Flatten(text);
        if (wanted.Length == 0) return null;

        List<AxNode> nodes = new();
        foreach (AxNode child in root.Children) Collect(child, nodes);
        return Pick(nodes, n => string.Equals(AxRoles.Flatten(n.Name), wanted, StringComparison.OrdinalIgnoreCase))
               ?? Pick(nodes, n => AxRoles.Flatten(n.Name).Contains(wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 把模型写来的目标规整成快照里的编号：快照里写的是 <c>[ref=e5]</c>，模型常照抄成 <c>ref=e5</c> 或带着方括号
    /// </summary>
    /// <param name="target">模型给的目标</param>
    /// <returns>像编号的规整成 <c>e5</c>；其余（看得见的文字）只去掉首尾空白</returns>
    public static string NormalizeTarget(string target)
    {
        string trimmed = target.Trim();
        string bare = trimmed.Trim('[', ']').Trim();
        if (bare.StartsWith("ref=", StringComparison.OrdinalIgnoreCase)) bare = bare[4..].Trim();
        return bare.Length > 1 && (bare[0] == 'e' || bare[0] == 'E') && bare[1..].All(char.IsAsciiDigit)
            ? "e" + bare[1..]
            : trimmed;
    }

    /// <summary>
    /// 从快照或正文里挑出含关键字的行（不分大小写），各带上下最近的一行非空行作参照
    /// （正文的段落之间隔着空行），不相邻的段之间用省略号隔开
    /// </summary>
    /// <param name="snapshot">快照或正文</param>
    /// <param name="keyword">关键字</param>
    /// <param name="withContext">是否捎带上下各一行；正文一行就是一整段，不必捎带</param>
    /// <returns>挑出的行；一行都没有时返回一句说明</returns>
    public static string FindLines(string snapshot, string keyword, bool withContext = true)
    {
        string[] lines = snapshot.Split('\n');
        SortedSet<int> keep = new();
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
            keep.Add(i);
            if (!withContext) continue;
            if (NearestText(lines, i, -1) is int above) keep.Add(above);
            if (NearestText(lines, i, 1) is int below) keep.Add(below);
        }

        if (keep.Count == 0) return $"(no line contains \"{keyword}\")";

        StringBuilder result = new();
        int previous = -1;
        foreach (int index in keep)
        {
            if (previous >= 0 && HasTextBetween(lines, previous, index)) result.AppendLine("…");
            result.AppendLine(lines[index].TrimEnd('\r'));
            previous = index;
        }

        return result.ToString().TrimEnd();
    }

    private static int? NearestText(string[] lines, int from, int step)
    {
        for (int i = from + step; i >= 0 && i < lines.Length; i += step)
        {
            if (lines[i].Trim().Length > 0) return i;
        }

        return null;
    }

    // 中间隔着的只是空行（段落间距）不算断开
    private static bool HasTextBetween(string[] lines, int from, int to)
    {
        for (int i = from + 1; i < to; i++)
        {
            if (lines[i].Trim().Length > 0) return true;
        }

        return false;
    }

    private static void Collect(AxNode node, List<AxNode> into)
    {
        if (node.Name.Length > 0 && !AxRoles.IsSkipped(node)) into.Add(node);
        foreach (AxNode child in node.Children) Collect(child, into);
    }

    private static AxNode? Pick(List<AxNode> nodes, Func<AxNode, bool> match) =>
        nodes.FirstOrDefault(n => AxRoles.IsActionable(n) && match(n)) ?? nodes.FirstOrDefault(match);
}
