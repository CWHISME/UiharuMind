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
/// 命中限幅、正文超预算整页换成命中地图（<see cref="GrepHitMap"/>）、附带说明。纯转换，不碰文件系统。
///
/// 刻意返回 string 而不是结构化对象：工具结果要落历史、被序列化再读回，对象会在重放时
/// 变成 <c>JsonElement</c>，其 ToString 吐出的是紧凑 JSON（换行变 <c>\n</c> 字面量、全挤一行）；
/// 字符串则还原成真实换行，模型与界面都拿到一行行的正文（见 ADR 0017 后的去结构化讨论）。
/// 界面的文件搜索读的是 <see cref="SimpleGrepper"/> 的逐条命中，不经这里，所以这里只为模型优化。
/// </summary>
internal static class GrepResultShaper
{
    internal const int MaxMatches = 200; //命中处上限(只限工具边界,界面文件搜索仍全量)
    /// <summary>
    /// 单行截断。截的是行尾，命中在截断点之后就看不见了，所以不能太小：
    /// 历史转写按 400 字符折行（HistoryTranscript.WrapChars），命中可以落在行内任意位置
    /// </summary>
    internal const int MaxLineChars = 500;

    /// <summary>
    /// 正文输出预算，按 <b>UTF-8 字节</b>算。超了整页换成命中地图。
    /// 与 Read 同为 16KB：一次搜索约 4–5k token，本地小模型的上下文经不起每次搜索吃掉近万
    /// </summary>
    internal const int MaxOutputBytes = 16 * 1024;

    private const int MaxOutlinedFiles = 30; //标注所在结构要逐个读文件，命中文件多时只标前这么多个

    /// <summary>
    /// 塑形一次搜索结果，返回给模型的纯文本
    /// </summary>
    /// <param name="outcome">搜索器的结构化结果</param>
    /// <param name="pattern">模型传入的原始表达式（用于说明降级与归一化）</param>
    /// <param name="filesOnly">只要命中分布（文件 + 命中数 + 行号），不要匹配内容</param>
    /// <param name="outlineOf">按命中文件的路径取大纲，用来标注命中所在的结构；null 不标注</param>
    /// <param name="contextLines">调用方要的上下文行数；退成分布地图时据此说明上下文没给</param>
    /// <returns>回给模型的工具结果正文（失败、说明一律拼在同段文本里）</returns>
    public static string Shape(GrepOutcome outcome, string pattern, bool filesOnly = false,
        Func<string, IReadOnlyList<OutlineEntry>?>? outlineOf = null, int contextLines = 0)
    {
        if (outcome.Failure != null)
        {
            return SearchFailureRenderer.Render(outcome.Failure, FileToolNames.Grep);
        }

        if (outcome.Matches.Count == 0) return BuildNotice(outcome, pattern, 0);
        if (filesOnly) return ShapeFilesOnly(outcome, pattern);

        GroupedHits grouped = GroupByFile(outcome.Matches);
        string body = RenderBody(grouped.Files.Select(file => (file.File, SplitBlocks(file.Lines, null))));
        bool tooWide = grouped.DroppedMatches > 0 || Encoding.UTF8.GetByteCount(body) > MaxOutputBytes;
        if (!tooWide)
        {
            // 走不走地图只看正文本身；确定返回正文后再补标注，补完超预算就不补——标注不能把正文挤成地图
            if (outlineOf != null && grouped.Files.Count <= MaxOutlinedFiles)
            {
                string annotated = RenderBody(grouped.Files.Select(file =>
                    (file.File, SplitBlocks(file.Lines, outlineOf(file.File)))));
                if (Encoding.UTF8.GetByteCount(annotated) <= MaxOutputBytes) body = annotated;
            }

            return ToolResultText.Compose(BuildNotice(outcome, pattern, grouped.Files.Count), body);
        }

        // 命中过多：直接给分布地图，不再返半截正文——半截按扫描序取、无相关度排序，
        // 模型会锚定到运气好的文件上（地图按全量统计）。开头先声明「这不是正文」，
        // 小模型容易从第一行就照着做，放在末尾的说明它未必读到
        GrepHitMap.Rendering map = GrepHitMap.Render(outcome.Matches, orderByPath: false, MaxOutputBytes);
        List<string> parts = QueryNotes(outcome, pattern);
        parts.Add($"{CountMatchLines(outcome.Matches)} 行命中，跨 {map.FileCount} 个文件，范围太宽，无法内联返回"
                  + (map.IsDirectoryLevel ? "，下面是按目录的命中分布，不是匹配内容。" : "，下面是按文件的命中分布，不是匹配内容。"));
        if (contextLines > 0) parts.Add($"所要的 {contextLines} 行上下文也一并略去。");
        string guidance = map.IsDirectoryLevel
            ? $"已按目录聚合为 {map.GroupCount} 组。{map.Legend}{ToolResultText.NarrowGuidance}。"
            : $"可按行号 `Read` 上面的文件，或{ToolResultText.NarrowGuidance}。";
        return ToolResultText.Compose(string.Join(" ", parts), map.Body, guidance);
    }

    // 文件清单按路径排：同目录的文件挨在一起，模型扫得出结构
    private static string ShapeFilesOnly(GrepOutcome outcome, string pattern)
    {
        GrepHitMap.Rendering map = GrepHitMap.Render(outcome.Matches, orderByPath: true, MaxOutputBytes);
        List<string> parts = QueryNotes(outcome, pattern);
        parts.Add($"共 {CountMatchLines(outcome.Matches)} 行命中，跨 {map.FileCount} 个文件。");
        if (!map.IsDirectoryLevel) return ToolResultText.Compose(string.Join(" ", parts), map.Body);

        parts.Add("文件太多，下面是按目录的命中分布。");
        return ToolResultText.Compose(string.Join(" ", parts), map.Body,
            $"已按目录聚合为 {map.GroupCount} 组。{map.Legend}{ToolResultText.NarrowGuidance}。");
    }

    /// <summary>一个文件去重排好的命中与上下文行</summary>
    private readonly record struct FileLines(string File, List<PlacedLine> Lines);

    private readonly record struct GroupedHits(List<FileLines> Files, int DroppedMatches);

    // 限幅按"命中处"计(一处命中连同它的上下文行算一条)
    private static GroupedHits GroupByFile(IReadOnlyList<GrepMatchResult> matches)
    {
        var linesByFile = new Dictionary<string, SortedDictionary<int, PlacedLine>>(StringComparer.Ordinal);
        var fileOrder = new List<string>();
        int droppedMatches = 0;
        int taken = 0;

        foreach (GrepMatchResult match in matches)
        {
            if (taken >= MaxMatches)
            {
                droppedMatches++;
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
                lines[line.LineNumber] = new PlacedLine(line.LineNumber, line.IsMatch,
                    $"{line.LineNumber}{separator}{ToolOutputTruncation.TruncateLine(line.Line, MaxLineChars)}");
            }
        }

        // 文件按路径字典序输出,不随扫描顺序变——输出稳定,模型逐文件扫读时
        // 顺序可预期;每文件内的行号仍升序(Read offset 照常可用)
        List<FileLines> files = fileOrder
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(file => new FileLines(file, linesByFile[file].Values.ToList()))
            .ToList();
        return new GroupedHits(files, droppedMatches);
    }

    /// <summary>同一行只保留一条时的排放记录：行号、是否命中行、渲染文本。命中行比上下文行优先</summary>
    private readonly record struct PlacedLine(int LineNumber, bool IsMatch, string Text);

    // 每个连续块（路径行或 -- 之后）至多一行块头 [位于 起–止] 签名，标这块首个命中所在的结构：模型不必为确认
    // 「这处命中在哪个方法里」再 Read 一次。块头只在块首，归属永远指向下面那块；与上一块同属一个结构就不重复。
    // 三种情况不标，标了只是重复或噪音：命中落在声明行本身（查定义）时标它的上一层；
    // 声明行已作为命中或上下文显示出来；整份文件只有一个顶层结构（一个文件一个类，几乎每处命中都在它里面）
    private static List<Block> SplitBlocks(List<PlacedLine> lines, IReadOnlyList<OutlineEntry>? outline)
    {
        var blocks = new List<Block>();
        int start = 0;
        for (int i = 1; i <= lines.Count; i++)
        {
            if (i < lines.Count && lines[i].LineNumber == lines[i - 1].LineNumber + 1) continue;
            blocks.Add(new Block(null, lines.GetRange(start, i - start)));
            start = i;
        }

        if (outline is not { Count: > 0 }) return blocks;

        int minDepth = outline.Min(entry => entry.Depth);
        OutlineEntry? soleRoot = outline.Count(entry => entry.Depth == minDepth) == 1
            ? outline.First(entry => entry.Depth == minDepth)
            : null;
        var shown = new HashSet<int>(lines.Select(line => line.LineNumber));
        OutlineEntry? current = null;
        for (int i = 0; i < blocks.Count; i++)
        {
            PlacedLine anchor = blocks[i].Lines.FirstOrDefault(line => line.IsMatch, blocks[i].Lines[0]);
            OutlineEntry? scope = FileOutline.Enclosing(outline, anchor.LineNumber, excludeStartingHere: true);
            if (scope is { } entry && scope != current && scope != soleRoot && !shown.Contains(entry.Line))
            {
                blocks[i] = blocks[i] with { Header = $"[位于 {FileOutline.FormatRange(entry)}] {entry.Text}" };
            }

            current = scope;
        }

        return blocks;
    }

    /// <summary>一段行号连续的命中与上下文行，及其可选块头</summary>
    private readonly record struct Block(string? Header, List<PlacedLine> Lines);

    /// <summary>
    /// 渲成 ripgrep <c>--heading</c> 的样子：文件路径单独一行，下面接命中行 <c>行号:</c> 与上下文行 <c>行号-</c>，
    /// 行号不连续处单独一行 <c>--</c>。路径行本身即文件边界。字节预算按整段渲完的文本算（与 Read 同口径）。
    /// </summary>
    private static string RenderBody(IEnumerable<(string File, List<Block> Blocks)> files)
    {
        var sb = new StringBuilder();
        foreach ((string file, List<Block> blocks) in files)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(file);
            for (int i = 0; i < blocks.Count; i++)
            {
                if (i > 0) sb.Append("\n--");
                if (blocks[i].Header != null) sb.Append('\n').Append(blocks[i].Header);
                foreach (PlacedLine line in blocks[i].Lines) sb.Append('\n').Append(line.Text);
            }
        }

        return sb.ToString();
    }

    /// <summary>命中行数（同一行多处出现算一行，对齐 grep -c）</summary>
    internal static int CountMatchLines(IEnumerable<GrepMatchResult> matches) =>
        matches.SelectMany(match => match.MatchingLines.Where(line => line.IsMatch)
                .Select(line => (match.FileName, line.LineNumber)))
            .Distinct()
            .Count();

    private static string BuildNotice(GrepOutcome outcome, string pattern, int fileCount)
    {
        List<string> parts = QueryNotes(outcome, pattern);

        if (fileCount > 0)
        {
            // 命中总数:让模型一眼知道这次搜到多少,不必数正文行数
            parts.Add($"共 {CountMatchLines(outcome.Matches)} 行命中，跨 {fileCount} 个文件。");
        }
        else
        {
            // 0 命中要明说,与"没搜成"分开
            parts.Add($"已搜索 \"{outcome.ResolvedDirectory}\" —— 0 行命中；"
                      + "路径存在，只是没有匹配的内容。");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// 与查询表达式有关的说明：正则降级为字面串、前导通配符归一化。
    /// 正文与地图两种输出模式共享——切地图时不能把这两条丢给模型，
    /// 否则它会把"少了几条命中"归错原因、或不知道表达式被改写。
    /// 位于总数/分布之前是有意的：先讲清"这次搜的是什么"，再讲"搜到多少"。
    /// </summary>
    private static List<string> QueryNotes(GrepOutcome outcome, string pattern)
    {
        List<string> parts = [];
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

        return parts;
    }
}
