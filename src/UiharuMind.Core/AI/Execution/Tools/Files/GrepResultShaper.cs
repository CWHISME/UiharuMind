/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 把 <see cref="GrepOutcome"/> 塑成回给模型的<b>纯文本</b>：按文件分组、渲成正文、
/// 命中限幅、正文超预算整页换成命中地图、附带说明。纯转换，不碰文件系统。
///
/// 刻意返回 string 而不是结构化对象：工具结果要落历史、被序列化再读回，对象会在重放时
/// 变成 <c>JsonElement</c>，其 ToString 吐出的是紧凑 JSON（换行变 <c>\n</c> 字面量、全挤一行）；
/// 字符串则还原成真实换行，模型与界面都拿到一行行的正文（见 ADR 0017 后的去结构化讨论）。
/// 界面的文件搜索读的是 <see cref="SimpleGrepper"/> 的逐条命中，不经这里，所以这里只为模型优化。
/// </summary>
internal static class GrepResultShaper
{
    internal const int MaxMatches = 200; //命中处上限(只限工具边界,界面文件搜索仍全量)
    internal const int MaxLineChars = 500; //单行截断

    /// <summary>正文输出预算，按 <b>UTF-8 字节</b>算（与 Read 同口径）。超了整页换成命中地图</summary>
    internal const int MaxOutputBytes = 32 * 1024;

    /// <summary>地图最多列出的文件数（按命中数降序取 Top N；命中极度分散本身就是"搜宽了"的信号）</summary>
    internal const int MaxMapFiles = 50;

    /// <summary>
    /// 塑形一次搜索结果，返回给模型的纯文本
    /// </summary>
    /// <param name="outcome">搜索器的结构化结果</param>
    /// <param name="pattern">模型传入的原始表达式（用于说明降级与归一化）</param>
    /// <returns>回给模型的工具结果正文（失败、说明一律拼在同段文本里）</returns>
    public static string Shape(GrepOutcome outcome, string pattern)
    {
        if (outcome.Failure != null)
        {
            return SearchFailureRenderer.Render(outcome.Failure, FileToolNames.Grep);
        }

        GroupedHits grouped = GroupByFile(outcome.Matches);
        string body = RenderBody(grouped.Files);
        if (Encoding.UTF8.GetByteCount(body) > MaxOutputBytes) return BuildHitMapText(outcome.Matches);

        return ToolResultText.AppendNotice(body, BuildNotice(outcome, pattern, grouped));
    }

    private readonly record struct GroupedHits(List<GrepFileHits> Files, int DroppedMatches, int DroppedFiles);

    // 限幅按"命中处"计(一处命中连同它的上下文行算一条)
    private static GroupedHits GroupByFile(IReadOnlyList<GrepMatchResult> matches)
    {
        var linesByFile = new Dictionary<string, SortedDictionary<int, PlacedLine>>(StringComparer.Ordinal);
        var fileOrder = new List<string>();
        var droppedFiles = new HashSet<string>(StringComparer.Ordinal);
        int droppedMatches = 0;
        int taken = 0;

        foreach (GrepMatchResult match in matches)
        {
            if (taken >= MaxMatches)
            {
                droppedMatches++;
                droppedFiles.Add(match.FileName);
                continue;
            }

            taken++;
            if (!linesByFile.TryGetValue(match.FileName, out SortedDictionary<int, PlacedLine>? lines))
            {
                linesByFile[match.FileName] = lines = new SortedDictionary<int, PlacedLine>();
                fileOrder.Add(match.FileName);
            }

            // 同一文件多处命中的上下文会互相重叠,同一行只留一条。命中优先于上下文:
            // 命中行可覆盖已占位的上下文行,上下文行不覆盖任何已有行——否则先到的上下文
            // 会把后到的命中挤成 "2-",模型把它当作附带上下文,就少读了一个真实命中。
            // 命中行用 : 分隔行号,上下文行用 -(对齐 ripgrep)
            foreach (GrepMatchLine line in match.MatchingLines)
            {
                if (lines.TryGetValue(line.LineNumber, out PlacedLine existing)
                    && (existing.IsMatch || !line.IsMatch)) continue;

                char separator = line.IsMatch ? ':' : '-';
                lines[line.LineNumber] = new PlacedLine(line.IsMatch,
                    $"{line.LineNumber}{separator}{ToolOutputTruncation.TruncateLine(line.Line, MaxLineChars)}");
            }
        }

        List<GrepFileHits> files = fileOrder
            .Select(file => new GrepFileHits { File = file, Lines = linesByFile[file].Values.Select(p => p.Text).ToList() })
            .ToList();
        return new GroupedHits(files, droppedMatches, droppedFiles.Count);
    }

    /// <summary>同一行只保留一条时的排放记录：是否命中行 + 渲染文本。命中行比上下文行优先</summary>
    private readonly record struct PlacedLine(bool IsMatch, string Text);

    /// <summary>
    /// 把分组后的命中组渲成一段文本：文件路径单独一行，下面接它的命中行与上下文行
    /// （命中 <c>行号: 内容</c>、上下文 <c>行号- 内容</c>）。路径行本身即文件边界。
    /// 每行末尾一个 <c>\n</c>，字节预算按整段渲完的文本算（与 Read 同口径）。
    /// </summary>
    private static string RenderBody(List<GrepFileHits> files)
    {
        var sb = new StringBuilder();
        foreach (GrepFileHits file in files)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(file.File).Append('\n');
            sb.Append(string.Join('\n', file.Lines));
        }

        return sb.ToString();
    }

    // 刻意不给正文:半截正文按扫描序取、无相关度排序,留着只会让模型锚定到运气好的文件上。
    // 地图按全量命中统计(不随 MaxMatches 截短),让模型先判断是不是搜宽了,再定点 Read 或收窄重搜。
    // 地图只给文件级分布、不给 snippet:摘片段同样是按扫描序取、无相关度排序,给了就是
    // 在"半截正文"之外再造一个会误导锚定的入口——snippet 的预览价值换不来这个风险。
    // 这里渲成文本行(每文件一行)而不是对象列表:对象跨历史边界会被 JSON 序列化成紧凑的一坨,
    // 与 Shape 返回值用 string 是同一个理由。说明经 AppendNotice 跟在分隔行后,与正文模式同口径。
    private static string BuildHitMapText(IReadOnlyList<GrepMatchResult> matches)
    {
        List<IGrouping<string, GrepMatchResult>> byFile =
            matches.GroupBy(match => match.FileName, StringComparer.Ordinal).ToList();

        List<MapEntry> map = byFile
            .Select(ToMapEntry)
            .OrderByDescending(entry => entry.Hits)
            .ThenBy(entry => entry.File, StringComparer.Ordinal)
            .Take(MaxMapFiles)
            .ToList();

        string notice = $"{matches.Count} 处命中，跨 {byFile.Count} 个文件——太宽，无法内联返回。"
                        + $"按命中数展示前 {map.Count} 个文件；`Read` 下面的文件，"
                        + "或收窄查询（fileGlobs/path）而不是换更宽的词重搜。";

        StringBuilder sb = new(map.Count * 64);
        foreach (MapEntry entry in map)
        {
            sb.Append(entry.File).Append(": ").Append(entry.Hits).Append(" 处命中（第 ")
              .Append(entry.FirstLine).Append("–").Append(entry.LastLine).Append(" 行）").Append('\n');
        }

        return ToolResultText.AppendNotice(sb.ToString().TrimEnd('\n'), notice);
    }

    /// <summary>地图条目：文件、命中数、行号区间</summary>
    private readonly record struct MapEntry(string File, int Hits, int FirstLine, int LastLine);

    private static MapEntry ToMapEntry(IGrouping<string, GrepMatchResult> file)
    {
        List<int> matchLines = file
            .SelectMany(match => match.MatchingLines)
            .Where(line => line.IsMatch)
            .Select(line => line.LineNumber)
            .ToList();

        return new MapEntry(
            file.Key,
            file.Count(),
            matchLines.Count > 0 ? matchLines.Min() : int.MaxValue,
            matchLines.Count > 0 ? matchLines.Max() : 0);
    }

    private static string? BuildNotice(GrepOutcome outcome, string pattern, GroupedHits grouped)
    {
        List<string> parts = [];

        // 降级必须说:模型以为传的是正则,实际按字面搜的,不说它会把"少了几条命中"归错原因
        if (outcome.FellBackToLiteral)
        {
            parts.Add($"\"{pattern}\" 编译不成受支持的正则"
                      + "（backreference 与 lookahead 不被支持），已按字面串重搜。"
                      + "刻意要按字面搜时请传 isRegex false。");
        }
        else if (!string.Equals(outcome.EffectiveQuery, pattern, StringComparison.Ordinal))
        {
            parts.Add($"模式已归一化为 \"{outcome.EffectiveQuery}\"——"
                      + "前导通配符的意思是「任意内容」。");
        }

        // 0 命中要明说,与"没搜成"分开
        if (grouped.Files.Count == 0)
        {
            parts.Add($"已搜索 \"{outcome.ResolvedDirectory}\" —— 0 处命中；"
                      + "路径存在，只是没有匹配的内容。");
        }

        if (grouped.DroppedMatches > 0)
        {
            parts.Add($"只展示前 {MaxMatches} 处命中；另有 {grouped.DroppedMatches} 处、"
                      + $"跨 {grouped.DroppedFiles} 个文件被丢弃。"
                      + "收窄查询，或限定 fileGlobs/path 范围。");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
