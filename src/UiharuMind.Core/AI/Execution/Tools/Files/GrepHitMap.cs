using System.Text;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// Grep 的命中分布：每文件一行（命中数 + 前几个命中行号），文件太多时按目录聚合。
/// 两处用到：命中过宽时代替正文；<c>filesOnly</c> 模式的正文本身。
///
/// 只给行号不给 snippet：摘片段同样按扫描序取、无相关度排序，会造出又一个误导锚定的入口。
/// 给前几个具体行号而不是「第 a–b 行」区间：区间跨几千行时没有定位价值，具体行号能直接 <c>Read offset=</c>。
/// </summary>
internal static class GrepHitMap
{
    /// <summary>
    /// 文件级的文件数上限，超过升目录级。刻意高于 <see cref="PathGrouping.MaxGroups"/>（50）——
    /// 文件级一行约 80 字符，100 行也不到 10KB，没必要文件刚过 50 个就丢行号入口
    /// </summary>
    internal const int MaxFileEntries = 100;

    private const int MaxListedLines = 5; //每文件列出的命中行号数

    /// <summary>渲染结果：正文、是否目录级、文件数，以及目录级时的组数与标签图例</summary>
    internal readonly record struct Rendering(string Body, bool IsDirectoryLevel, int FileCount, int GroupCount,
        string? Legend);

    /// <summary>
    /// 渲染命中分布。默认按路径字典序：同目录的文件挨在一起，模型扫得出结构，
    /// 输出被截断时也能从路径猜出少了哪块——热点榜会把同一模块的文件拆散到列表各处。
    /// 命中数降序保留为<b>兜底</b>选项（热点视图），未来需要「先看最重要的文件」时传 false
    /// </summary>
    /// <param name="matches">全量命中</param>
    /// <param name="orderByPath">true 按路径字典序（默认）；false 按命中数降序（兜底的热点视图）</param>
    /// <param name="maxBytes">文件级正文预算，超了降级到目录级</param>
    /// <returns>渲染结果</returns>
    internal static Rendering Render(IReadOnlyList<GrepMatchResult> matches, bool orderByPath, int maxBytes)
    {
        List<IGrouping<string, GrepMatchResult>> byFile =
            matches.GroupBy(match => match.FileName, StringComparer.Ordinal).ToList();

        if (byFile.Count <= MaxFileEntries)
        {
            string fileBody = RenderFiles(byFile, orderByPath);
            // 路径可能很长（工作区外绝对路径），文件级渲完仍超预算就降级到目录级。
            // 先渲再量会白渲一次，但最多 100 行、几 KB，不值得为此预估长度
            if (Encoding.UTF8.GetByteCount(fileBody) <= maxBytes)
            {
                return new Rendering(fileBody, false, byFile.Count, 0, null);
            }
        }

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(
            byFile.Select(file => (file.Key, GrepResultShaper.CountMatchLines(file))).ToList());
        string dirBody = string.Join('\n',
            groups.Select(group => $"{group.Prefix}: {group.Total} 行 / {group.Count} 个文件"));
        return new Rendering(dirBody, true, byFile.Count, groups.Count, PathGrouping.DescribeLabels(groups));
    }

    private static string RenderFiles(List<IGrouping<string, GrepMatchResult>> byFile, bool orderByPath)
    {
        var files = byFile.Select(file => (File: file.Key, Lines: file
                .SelectMany(match => match.MatchingLines)
                .Where(line => line.IsMatch)
                .Select(line => line.LineNumber)
                .Distinct()
                .Order()
                .ToList()))
            .ToList();
        var ordered = orderByPath
            ? files.OrderBy(file => file.File, StringComparer.Ordinal)
            : files.OrderByDescending(file => file.Lines.Count).ThenBy(file => file.File, StringComparer.Ordinal);

        var sb = new StringBuilder(byFile.Count * 64);
        foreach ((string file, List<int> lines) in ordered)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(file).Append(": ").Append(lines.Count).Append(" 行");
            if (lines.Count == 0) continue;

            sb.Append('（').AppendJoin(", ", lines.Take(MaxListedLines));
            if (lines.Count > MaxListedLines) sb.Append(", …");
            sb.Append('）');
        }

        return sb.ToString();
    }
}
