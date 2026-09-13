/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Harness;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Files;

// [MFA绕坑] 绕:框架 FileAccessProvider 拒绝一切绝对路径 因:该类 internal 无法继承修改 删除条件:框架允许配置路径策略
/// <summary>
/// 自带的文件工具集,替代 MFA 内置的 FileAccessProvider。
/// MFA 的 FileAccessProvider 在调用存储前会用 StorePaths.NormalizeRelativePath 拒绝一切绝对路径,
/// 且该类为 internal 无法继承/修改;因此这里完全自行实现文件访问:
/// - 相对路径解析到工作区根目录;
/// - 绝对路径直接访问真实文件系统。
///
/// <b>工作区外的写入没有在这一层拦</b>,拦在审批规则里(<c>ApprovalModeMapper</c>):
/// 任何权限档下首次越界写入都要用户点一次,包括完全自动档。放在那一层是因为「越界」是<b>授权</b>
/// 问题而不是路径解析问题——同一条判据还要服务 shell,而且用户点了"本会话允许"之后要能真的放行。
///
/// 五个工具:Read / Write / Edit / Glob / Grep。
/// Glob 采用 Meziantou.Framework.Globbing 实现递归路径枚举;Grep 采用 Glacier.Grep 高性能检索引擎
/// (它自带 .gitignore/.ignore/.rgignore 的层级排除,对齐 ripgrep 行为)。
/// 编辑语义(唯一匹配/重叠检测/保守 fuzzy/落盘保真)全在 <see cref="FileEditPlanner"/>,
/// 本类只负责路径解析、限幅与落盘。写工具各包一层 ApprovalRequiredAIFunction,沿用 MFA 的审批管线。
/// </summary>
internal sealed class PermissiveFileAccessTools
{
    // —— 输出限幅:工具输出直接进模型上下文,编码会话的上下文大头是工具结果而非对话。
    //    Glob 已在 SimpleGlobber 内限 300 条;shell 由框架 MaxOutputBytes(64KiB)截断。——
    internal const int DefaultReadLineLimit = 2000; //未显式传 limit 时的行数上限

    /// <summary>
    /// Read 单次返回的总量上限,按 <b>UTF-8 字节</b>算。
    ///
    /// 曾按字符算(120_000,注释写"约 3 万 token"),那是英文的 4 字符/token；中文约 1~1.5 字符/token,
    /// 于是读一个中文文件实际能放进 8~12 万 token,是标称值的三四倍。本仓注释通篇中文、
    /// docs 更是纯中文,一次 Read 就能吃掉大半个上下文。按字节算则中英文都落在 1.5 万 token 上下。
    ///
    /// 64KB→1MB:远程大模型(DeepSeek 1M 上下文)读 3000 行文件需分 3 次,分段读撑爆本地小模型
    /// 上下文后引发截断重填、模型失忆、提前停手。1MB 能覆盖绝大多数源文件一次读完,
    /// 同时给本地小模型留一个安全天花板。传 limit=-1 可绕过此限制读全文。
    /// </summary>
    internal const int MaxReadTotalBytes = 32 * 1024;

    internal const int MaxReadLineChars = 2000; //单行截断(压缩产物一行可达数百 KB)
    internal const int MaxGrepMatches = 200; //Grep 命中上限(只限工具边界,UI 文件搜索仍全量)
    internal const int MaxGrepLineChars = 500; //Grep 单行截断

    /// <summary>Grep 正文输出预算,按 <b>UTF-8 字节</b>算(与 Read 同口径)。超了不返半截正文,
    /// 整页换成命中地图——半截正文按扫描序取、无相关度排序,留着只会让模型锚定到运气好的文件上。</summary>
    internal const int MaxGrepOutputBytes = 32 * 1024;

    /// <summary>地图模式最多列出的文件数(按命中数降序取 Top N;命中极度分散本身就是"搜宽了"的信号)</summary>
    internal const int MaxGrepMapFiles = 50;

    internal const int MaxEditDiffLines = 80; //Edit 回给模型的 diff 行数上限

    private readonly string _workspaceRoot;
    private readonly SimpleGlobber _glob;
    private readonly SimpleGrepper _grepper;

    public PermissiveFileAccessTools(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _glob = new SimpleGlobber(workspaceRoot);
        _grepper = new SimpleGrepper(workspaceRoot);
        Directory.CreateDirectory(_workspaceRoot);
    }

    public IReadOnlyList<AITool> Create(bool disableWriteTools = false)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(Read, ToolOptions(FileToolNames.Read)),
            AIFunctionFactory.Create(Glob, ToolOptions(FileToolNames.Glob)),
            AIFunctionFactory.Create(Grep, ToolOptions(FileToolNames.Grep)),
        };

        if (!disableWriteTools)
        {
            tools.Add(Wrap(AIFunctionFactory.Create(Write, ToolOptions(FileToolNames.Write))));
            tools.Add(Wrap(AIFunctionFactory.Create(Edit, ToolOptions(FileToolNames.Edit))));
        }

        return tools;

        static AITool Wrap(AIFunction function) => new ApprovalRequiredAIFunction(function);

        // 宽容口径:fileGlobs 是 string[],而模型常给一个标量字符串,
        // 从前那会死在反序列化上并回一句它看不懂的框架异常(见 ToolJson)
        static AIFunctionFactoryOptions ToolOptions(string name) =>
            new() { Name = name, SerializerOptions = ToolJson.Lenient };
    }

    [Description("Find files by glob pattern.")]
    private async Task<GlobToolResult> Glob(
        [Description("Glob pattern, e.g. \"**/*.cs\".")] string pattern,
        [Description("Omit this: by default the whole working directory is searched. "
                     + "Pass it only to narrow the search to one subdirectory, "
                     + "relative to the working directory. Returned paths are relative to the "
                     + "working directory either way, so you can pass them straight to `Read`.")]
        string? directory = null)
    {
        GlobOutcome outcome = await _glob.SearchAsync(pattern, directory).ConfigureAwait(false);

        if (outcome.Failure != null)
        {
            return new GlobToolResult
            {
                Notice = SearchFailureRenderer.Render(outcome.Failure, FileToolNames.Grep),
            };
        }

        // 0 命中要明说。裸一个空列表分不出"搜过了确实没有"与"根本没搜成",
        // 而模型对这两种情况该做的下一步完全不同
        // 面向模型的渲染只在这一层做:每个文件都标大小。纪律段要求它"绝不要把一整个大文件
        // 拉进上下文",却从来不给判断依据——这是那句要求唯一缺的东西。
        // 只标超阈值的文件试过,不行:没有标注就分不清"这个小"和"这个没测",
        // 一堆小文件全无标注也就没法排序、选不出该先读哪个。
        // 一条约 4 token,而一次误读大文件是上万 token,期望值上很划算
        // 文件在前、目录在后,各组内按路径。搜索器给的是纯路径字典序,那会让同一层的
        // [FILE] 与 [DIR] 逐行交替(README.md / cmake / data / default.nix / docs …),
        // 扫起来很费劲。文件才是可行动的东西——拿到路径下一步就是 Read/Edit,
        // 而 `**/*` 里的目录多数时候只是结构信息,压在后面即可。
        // 排序只在这一层做:界面那侧有自己的习惯(见 SearchService)
        return new GlobToolResult
        {
            Entries = outcome.Entries
                .OrderBy(x => x.IsDirectory)
                .ThenBy(x => x.Path, StringComparer.Ordinal)
                .Select(Render)
                .ToList(),
            Notice = BuildGlobNotice(outcome),
        };

        static string Render(GlobEntry entry) => entry.IsDirectory
            ? $"[DIR]  {entry.Path}"
            : $"[FILE] {entry.Path} ({GameUtils.FormatBytes(entry.SizeBytes)})";
    }

    private static string? BuildGlobNotice(GlobOutcome outcome)
    {
        if (outcome.Entries.Count == 0)
        {
            return $"Searched \"{outcome.ResolvedDirectory}\" — 0 matches. The directory exists; "
                   + "nothing matched the pattern.";
        }

        return outcome.Truncated
            ? $"Showing the first {outcome.Entries.Count} entries; more were dropped. "
              + "Narrow the pattern or scope it with directory."
            : null;
    }

    [Description("Search file contents. Respects .gitignore. If hits are too many, returns a hit map instead of inline lines: then narrow with fileGlobs/directory or Read the listed files — never re-search with a broader term.")]
    internal async Task<GrepToolResult> Grep(
        [Description("Search pattern (ripgrep syntax).")] string pattern,
        [Description("Treat the pattern as a regular expression. "
                     + "A pattern that does not compile as one is retried as a literal string, "
                     + "and the result says so.")]
        bool isRegex = true,
        [Description("Case-sensitive search.")] bool caseSensitive = false,
        [Description("How many lines of context to show around each match.")] int contextLines = 0,
        [Description("Maximum directory depth to walk (null means no limit).")] int? maxDepth = null,
        [Description("Only search files whose name matches one of these globs, e.g. \"*.cs\". "
                     + "Only the file name is matched - do NOT include a path or '**/' prefix "
                     + "(e.g. not \"**/*.cs\"); to limit the directory use the 'directory' argument. "
                     + "A leading '**/' or path in a glob is stripped to the bare file name.")]
        string[]? fileGlobs = null,
        [Description("Omit this: by default the whole working directory is searched. "
                     + "Pass it only to narrow the search to one subdirectory, "
                     + "relative to the working directory. Returned paths are relative to the "
                     + "working directory either way, so you can pass them straight to `Read`.")]
        string? directory = null,
        CancellationToken ct = default)
    {
        GrepOutcome outcome = await _grepper
            .SearchAsync(pattern, isRegex, caseSensitive, contextLines, maxDepth, fileGlobs, directory, ct)
            .ConfigureAwait(false);

        if (outcome.Failure != null)
        {
            return new GrepToolResult
            {
                Notice = SearchFailureRenderer.Render(outcome.Failure, FileToolNames.Grep),
            };
        }

        IReadOnlyList<GrepMatchResult> results = outcome.Matches;

        // 全量命中的每文件统计——地图模式用。引擎本来就全量返回,这里免费算;
        // 不随 200 上限截断,地图才能看到被丢掉的部分在哪。
        var fileStats = new Dictionary<string, (int Hits, int FirstLine, int LastLine, string Snippet)>(StringComparer.Ordinal);
        foreach (GrepMatchResult result in results)
        {
            if (!fileStats.TryGetValue(result.FileName, out var stat))
            {
                stat = (Hits: 0, FirstLine: int.MaxValue, LastLine: 0, Snippet: string.Empty);
            }

            int first = int.MaxValue;
            int last = 0;
            foreach (GrepMatchLine line in result.MatchingLines)
            {
                if (!line.IsMatch) continue;
                if (line.LineNumber < first) first = line.LineNumber;
                if (line.LineNumber > last) last = line.LineNumber;
            }

            stat.Hits++;
            if (first < stat.FirstLine) stat.FirstLine = first;
            if (last > stat.LastLine) stat.LastLine = last;
            if (stat.Snippet.Length == 0) stat.Snippet = result.Snippet;
            fileStats[result.FileName] = stat;
        }

        // 自有结果 → 工具结果的转换只发生在这里:按文件分组 + 每行 grep 味文本,
        // 命中限幅也只发生在这里。
        // 界面那侧要的是可逐条点开的命中列表,那层读的是 SimpleGrepper 的结构化结果,
        // 不经这里——所以这里可以放心为模型优化成紧凑的分组视图。
        var converted = new List<GrepFileHits>();
        var byFile = new Dictionary<string, GrepFileHits>(StringComparer.Ordinal);
        var pendingLines = new Dictionary<string, List<(int LineNumber, string Text)>>(StringComparer.Ordinal);
        var seenLines = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        int remaining = MaxGrepMatches;
        int droppedMatches = 0;
        HashSet<string> droppedFiles = new(StringComparer.Ordinal);

        foreach (GrepMatchResult result in results)
        {
            if (remaining <= 0)
            {
                droppedMatches++;
                droppedFiles.Add(result.FileName);
                continue;
            }

            // 限幅按“命中处”计(一处命中连同它的上下文行算一条);
            // 上下文重叠去重按文件计——同一文件多处命中的上下文会互相重叠,只保留一次
            remaining--;
            if (!byFile.TryGetValue(result.FileName, out GrepFileHits? file))
            {
                byFile[result.FileName] = file = new GrepFileHits { File = result.FileName };
                converted.Add(file);
                pendingLines[result.FileName] = [];
                seenLines[result.FileName] = [];
            }

            HashSet<int> seen = seenLines[result.FileName];
            foreach (GrepMatchLine line in result.MatchingLines)
            {
                if (!seen.Add(line.LineNumber)) continue;

                // 路径只在组头出现一次;行内只需行号 + 分隔符。
                // 命中行用 : 分隔行号,上下文行用 -(对齐 ripgrep)
                pendingLines[result.FileName].Add((line.LineNumber,
                    line.IsMatch
                        ? $"{line.LineNumber}:{TruncateLine(line.Line, MaxGrepLineChars)}"
                        : $"{line.LineNumber}-{TruncateLine(line.Line, MaxGrepLineChars)}"));
            }
        }

        // 组内按行号升序,模型一眼能数出“哪几处、第几行”
        foreach ((string fileName, List<(int, string)> lines) in pendingLines)
        {
            lines.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            byFile[fileName].Lines = lines.Select(x => x.Item2).ToList();
        }

        // 正文输出字节预算:超了整页换地图,不带半截正文。
        // 半截正文按扫描序取、无相关度排序,留着只会让模型锚定到运气好的文件上
        int bodyBytes = 0;
        foreach (GrepFileHits file in converted)
        {
            bodyBytes += Encoding.UTF8.GetByteCount(file.File) + 1;
            foreach (string line in file.Lines)
            {
                bodyBytes += Encoding.UTF8.GetByteCount(line) + 1;
            }
        }

        if (bodyBytes > MaxGrepOutputBytes)
        {
            return BuildHitMap();
        }

        // 说明走 Notice 字段,不再塞一条 FileName = "[truncated]" 的假命中:
        // 那种假条目正是模型分不清"命中"与"一句话"的来源
        return new GrepToolResult { Matches = converted, Notice = BuildGrepNotice() };

        GrepToolResult BuildHitMap()
        {
            List<GrepMapEntry> map = fileStats
                .OrderByDescending(x => x.Value.Hits)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .Take(MaxGrepMapFiles)
                .Select(x => new GrepMapEntry
                {
                    File = x.Key,
                    Hits = x.Value.Hits,
                    FirstLine = x.Value.FirstLine,
                    LastLine = x.Value.LastLine,
                    Snippet = TruncateLine(x.Value.Snippet, MaxGrepLineChars),
                })
                .ToList();

            // 刻意不给正文:模型第一反应应是判断"词是不是搜宽了",而不是将就着读半截扫描序正文。
            // 定点 Read 或收窄重搜都行;唯独别换更宽的词重搜——那会把刚截掉的内容原样再灌一遍(回灌)
            string notice = $"{results.Count} matches across {fileStats.Count} file(s) — too broad to return inline. "
                            + $"Showing the top {map.Count} files by hit count; `Read` the files below, "
                            + "or narrow the query (fileGlobs/directory) instead of re-searching with a broader term.";

            return new GrepToolResult { Matches = [], Map = map, Notice = notice };
        }

        string? BuildGrepNotice()
        {
            List<string> parts = [];

            // 降级必须说。模型以为自己传的是正则,实际按字面搜的,
            // 不说一声它对"为什么少了几条命中"会推错
            if (outcome.FellBackToLiteral)
            {
                parts.Add($"\"{pattern}\" does not compile as a regular expression, "
                          + "so it was searched as a literal string. "
                          + "Pass isRegex false to do that on purpose.");
            }
            else if (!string.Equals(outcome.EffectiveQuery, pattern, StringComparison.Ordinal))
            {
                parts.Add($"The pattern was normalised to \"{outcome.EffectiveQuery}\" "
                          + "so that a leading wildcard means \"anything\".");
            }

            if (converted.Count == 0)
            {
                parts.Add($"Searched \"{outcome.ResolvedDirectory}\" — 0 matches. "
                          + "The directory exists; nothing there matched.");
            }

            if (droppedMatches > 0)
            {
                parts.Add($"Showing the first {MaxGrepMatches} matches; {droppedMatches} more "
                          + $"across {droppedFiles.Count} file(s) were dropped. "
                          + "Narrow the query, or scope it with fileGlobs/directory.");
            }

            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
    }

    /// <summary>超长行截断(限幅只服务工具输出,不改动底层搜索结果)</summary>
    internal static string TruncateLine(string text, int maxChars)
    {
        return text.Length <= maxChars ? text : text[..maxChars] + " …[truncated]";
    }

    [Description("""
                 Read a file's raw content.
                 - Lines are separated by newlines. The first line of your mental model is line 1.
                 - By default at most 2000 lines or 1MB are returned per call, whichever comes first;
                   a trailing notice tells you the offset to continue from.
                 - Pass limit=-1 to read the entire file in one call, bypassing the byte cap.
                   Use this when you need to understand the whole file for refactoring.
                 - When you only need part of a large file, locate the interesting lines with Grep first,
                   then read a slice with offset/limit — don't pull the whole file for a detail.
                 """)]
    internal Task<string> Read(
        [Description("File path, absolute or relative to the working directory.")] string filePath,
        [Description("1-based starting line.")] int offset = 1,
        [Description("Max lines to return. Pass -1 to read the entire file (bypasses the 1MB byte cap). " +
                     "When omitted, defaults to 2000 lines.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        string full = ResolvePath(filePath);
        if (!File.Exists(full)) return Task.FromResult($"File '{filePath}' not found.");

        if (offset < 1) offset = 1;
        // limit=-1:全文读模式,绕过字节上限,读完整个文件
        // 其余:不传 limit 时套默认窗口,总量另设保险——
        // 编码会话一次误读大文件就是几万 token,截断必须由工具侧兜底
        bool fullFile = limit == -1;
        int effectiveLimit = fullFile ? int.MaxValue : Math.Max(1, limit ?? DefaultReadLineLimit);

        var lines = new List<string>();
        bool hasMore = false;
        int totalBytes = 0;
        using var reader = new StreamReader(full, Encoding.UTF8);
        for (int current = 1; current < offset; current++)
        {
            if (reader.ReadLine() is null) break;
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!fullFile && (lines.Count >= effectiveLimit || totalBytes >= MaxReadTotalBytes))
            {
                hasMore = true;
                break;
            }

            if (line.Length > MaxReadLineChars) line = TruncateLine(line, MaxReadLineChars);
            totalBytes += Encoding.UTF8.GetByteCount(line) + 1;
            lines.Add(line);
        }

        if (lines.Count == 0) return Task.FromResult($"File '{filePath}' is empty or offset is beyond its end.");

        string content = string.Join('\n', lines);
        if (!hasMore) return Task.FromResult(content);

        int nextOffset = offset + lines.Count;
        // 带上文件总大小:光说"续读 offset=31",模型不知道是再翻一页就完还是要翻六十页,
        // 也就无从判断该继续翻还是改用 `Grep` 定位。总行数会更贴(offset/limit 按行算),
        // 但那要读到文件末尾才知道——正好违背这个工具存在的意义;文件大小是 O(1) 的代理值
        string size = GameUtils.FormatBytes(new FileInfo(full).Length);
        return Task.FromResult(
            $"{content}\n…[truncated: showing lines {offset}–{nextOffset - 1} of a {size} file; "
            + $"continue with offset={nextOffset}]");
    }

    [Description("Create a new file, or replace an existing one wholesale. Use 'Edit' for partial changes.")]
    private async Task<string> Write(
        [Description("File path, absolute or relative to the working directory.")]
        string filePath,
        [Description("Full file content.")] string content,
        [Description("Must be true to overwrite an existing file.")]
        bool overwrite = false,
        CancellationToken ct = default)
    {
        string full = ResolvePath(filePath);
        bool exists = File.Exists(full);
        if (exists && !overwrite)
            return "File exists. Set overwrite=true to replace it, or use 'Edit' to change part of it.";

        // 覆盖已有文件时沿用它的 BOM 与行尾风格;新建文件则是无 BOM + 模型给的 \n。
        // 不这么做的话,让模型重写一个 CRLF 文件会顺手把整份文件的行尾改掉
        TextFileEnvelope envelope = exists
            ? TextFileEnvelope.FromBytes(await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false))
            : TextFileEnvelope.FromText(string.Empty);

        await SaveAsync(full, envelope, envelope.ConvertNewLines(content), ct).ConfigureAwait(false);
        return $"Saved '{filePath}' ({content.Split('\n').Length} lines).";
    }

    [Description("""
                 Change an existing file by exact text replacement.
                 - Put every change to one file in a single call, as multiple entries in `edits`.
                 - Every edits[].oldString is matched against the file as it is now, not against
                   your earlier entries in the same call, and must match exactly one place.
                 - Entries must not overlap. Merge nearby changes into one entry instead.
                 - Nothing is written unless every entry applies; the error tells you what to fix.
                 """)]
    private async Task<string> Edit(
        [Description("File path, absolute or relative to the working directory.")]
        string filePath,
        [Description("The replacements to make, all matched against the current file content.")]
        List<FileEdit> edits,
        CancellationToken ct = default)
    {
        string full = ResolvePath(filePath);
        FileEditPlan plan = await FileEditPlanner.PlanFileAsync(full, filePath, edits, ct).ConfigureAwait(false);
        if (!plan.Succeeded) return $"[Edit failed] {plan.Error}";

        await SaveAsync(full, plan.Envelope, plan.NewText, ct).ConfigureAwait(false);

        string diff = FileEditPlanner.RenderDiff(plan.Diff, MaxEditDiffLines);
        return $"Applied {edits.Count} edit(s) to '{filePath}'.\n{diff}";
    }

    //统一落盘:BOM 与行尾按信封原样还原
    private static Task SaveAsync(string full, TextFileEnvelope envelope, string content, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return File.WriteAllBytesAsync(full, envelope.ToBytes(content), ct);
    }

    // ---- 路径解析 ----

    private string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_workspaceRoot, path));
    }
}
