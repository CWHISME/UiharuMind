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
/// 把 <see cref="GrepOutcome"/> 塑成回给模型的 <see cref="GrepToolResult"/>：按文件分组、命中限幅、
/// 正文超预算整页换成命中地图、附带说明。纯转换，不碰文件系统。
///
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
    /// 塑形一次搜索结果
    /// </summary>
    /// <param name="outcome">搜索器的结构化结果</param>
    /// <param name="pattern">模型传入的原始表达式（用于说明降级与归一化）</param>
    /// <returns>回给模型的工具结果</returns>
    public static GrepToolResult Shape(GrepOutcome outcome, string pattern)
    {
        if (outcome.Failure != null)
        {
            return new GrepToolResult { Notice = SearchFailureRenderer.Render(outcome.Failure, FileToolNames.Grep) };
        }

        GroupedHits grouped = GroupByFile(outcome.Matches);
        if (BodyBytes(grouped.Files) > MaxOutputBytes) return BuildHitMap(outcome.Matches);

        return new GrepToolResult { Matches = grouped.Files, Notice = BuildNotice(outcome, pattern, grouped) };
    }

    private readonly record struct GroupedHits(List<GrepFileHits> Files, int DroppedMatches, int DroppedFiles);

    // 限幅按"命中处"计(一处命中连同它的上下文行算一条)
    private static GroupedHits GroupByFile(IReadOnlyList<GrepMatchResult> matches)
    {
        var linesByFile = new Dictionary<string, SortedDictionary<int, string>>(StringComparer.Ordinal);
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
            if (!linesByFile.TryGetValue(match.FileName, out SortedDictionary<int, string>? lines))
            {
                linesByFile[match.FileName] = lines = new SortedDictionary<int, string>();
                fileOrder.Add(match.FileName);
            }

            // 同一文件多处命中的上下文会互相重叠,同一行只留先到的那条。
            // 命中行用 : 分隔行号,上下文行用 -(对齐 ripgrep)
            foreach (GrepMatchLine line in match.MatchingLines)
            {
                if (lines.ContainsKey(line.LineNumber)) continue;

                char separator = line.IsMatch ? ':' : '-';
                lines[line.LineNumber] =
                    $"{line.LineNumber}{separator}{ToolOutputTruncation.TruncateLine(line.Line, MaxLineChars)}";
            }
        }

        List<GrepFileHits> files = fileOrder
            .Select(file => new GrepFileHits { File = file, Lines = linesByFile[file].Values.ToList() })
            .ToList();
        return new GroupedHits(files, droppedMatches, droppedFiles.Count);
    }

    // 路径与每行各算一个分隔符
    private static int BodyBytes(List<GrepFileHits> files) =>
        files.Sum(file => Encoding.UTF8.GetByteCount(file.File) + 1
                          + file.Lines.Sum(line => Encoding.UTF8.GetByteCount(line) + 1));

    // 刻意不给正文:半截正文按扫描序取、无相关度排序,留着只会让模型锚定到运气好的文件上。
    // 地图按全量命中统计(不随 MaxMatches 截短),让模型先判断是不是搜宽了,再定点 Read 或收窄重搜
    private static GrepToolResult BuildHitMap(IReadOnlyList<GrepMatchResult> matches)
    {
        List<IGrouping<string, GrepMatchResult>> byFile =
            matches.GroupBy(match => match.FileName, StringComparer.Ordinal).ToList();

        List<GrepMapEntry> map = byFile
            .Select(ToMapEntry)
            .OrderByDescending(entry => entry.Hits)
            .ThenBy(entry => entry.File, StringComparer.Ordinal)
            .Take(MaxMapFiles)
            .ToList();

        string notice = $"{matches.Count} matches across {byFile.Count} file(s) — too broad to return inline. "
                        + $"Showing the top {map.Count} files by hit count; `Read` the files below, "
                        + "or narrow the query (fileGlobs/path) instead of re-searching with a broader term.";

        return new GrepToolResult { Matches = [], Map = map, Notice = notice };
    }

    private static GrepMapEntry ToMapEntry(IGrouping<string, GrepMatchResult> file)
    {
        List<int> matchLines = file
            .SelectMany(match => match.MatchingLines)
            .Where(line => line.IsMatch)
            .Select(line => line.LineNumber)
            .ToList();
        string snippet = file.FirstOrDefault(match => match.Snippet.Length > 0)?.Snippet ?? string.Empty;

        return new GrepMapEntry
        {
            File = file.Key,
            Hits = file.Count(),
            FirstLine = matchLines.Count > 0 ? matchLines.Min() : int.MaxValue,
            LastLine = matchLines.Count > 0 ? matchLines.Max() : 0,
            Snippet = ToolOutputTruncation.TruncateLine(snippet, MaxLineChars),
        };
    }

    private static string? BuildNotice(GrepOutcome outcome, string pattern, GroupedHits grouped)
    {
        List<string> parts = [];

        // 降级必须说:模型以为传的是正则,实际按字面搜的,不说它会把"少了几条命中"归错原因
        if (outcome.FellBackToLiteral)
        {
            parts.Add($"\"{pattern}\" does not compile as a supported regular expression "
                      + "(backreferences and lookarounds are not supported), "
                      + "so it was searched as a literal string. "
                      + "Pass isRegex false to do that on purpose.");
        }
        else if (!string.Equals(outcome.EffectiveQuery, pattern, StringComparison.Ordinal))
        {
            parts.Add($"The pattern was normalised to \"{outcome.EffectiveQuery}\" "
                      + "so that a leading wildcard means \"anything\".");
        }

        // 0 命中要明说,与"没搜成"分开
        if (grouped.Files.Count == 0)
        {
            parts.Add($"Searched \"{outcome.ResolvedDirectory}\" — 0 matches; "
                      + "the path exists, nothing there matched.");
        }

        if (grouped.DroppedMatches > 0)
        {
            parts.Add($"Showing the first {MaxMatches} matches; {grouped.DroppedMatches} more "
                      + $"across {grouped.DroppedFiles} file(s) were dropped. "
                      + "Narrow the query, or scope it with fileGlobs/path.");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
