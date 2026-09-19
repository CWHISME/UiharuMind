/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
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
/// 本类只负责路径解析、限幅与落盘;Write 覆盖前经 <see cref="IFileBackupStore"/> 自动备份。
/// 写工具各包一层 ApprovalRequiredAIFunction,沿用 MFA 的审批管线。
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
    /// 与 Grep 的地图阈值同口径(32KB≈8K token):默认读是一个安全窗口,远程大模型忘传 limit 也不会
    /// 一次灌进几十万 token。想要全文就走 <c>limit=-1</c> 的显式通道,Description 里已经写清。
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

    /// <summary>Edit 回给模型的 diff 行数上限（与审批卡片共用，见 <c>FileEditPlanner.DefaultMaxDiffLines</c>）</summary>
    internal const int MaxEditDiffLines = FileEditPlanner.DefaultMaxDiffLines;

    /// <summary>Edit diff 单行长度上限（minified JSON 一行可达几十 KB，行数上限拦不住）</summary>
    internal const int MaxEditDiffLineChars = FileEditPlanner.DefaultMaxDiffLineChars;

    /// <summary>
    /// 落盘的"读→计划→写"关键区不原子：<c>AllowConcurrentInvocation=true</c> 时同一轮
    /// 消息里两个工具调用并发执行，两个 Edit/Write 打同一文件就会 lost-update（后写覆盖前写），
    /// 而且是静默丢改动、连报错都没有。锁表是进程级静态的：主代理与子代理在同一个进程中
    /// 各自持有本类的实例，静态表让它们自然互斥；同文件不再同时读-改-写。
    ///
    /// 用<b>定长 striped 锁数组</b>而不是 <c>ConcurrentDictionary</c>：条目固定、内存有界、
    /// 永不回收（字典会随接触过的文件数无界增长，review 点名）。按路径 hash 取模,碰撞的
    /// 代价只是不同文件偶发伪串行——写路径毫秒级，无所谓。
    /// key 用 OrdinalIgnoreCase——macOS/Windows 默认文件系统大小写不敏感，
    /// <c>/a.cs</c> 与 <c>/A.cs</c> 是同一文件，不能各自拿一把锁。
    /// 已知边界：不做符号链接 realpath 归一，<c>/real/c.cs</c> 与 <c>/link→real/c.cs</c>
    /// 会拿到不同条纹——与 ApprovalModeMapper 的"不解析符号链接"口径一致，可接受。
    /// 注：Write/Edit 在进入锁前已解析<b>文件本体</b>的 symlink（ResolveWriteTarget），
    /// 所以"文件本身是链接"的场景已覆盖；未覆盖的只剩<b>父目录是链接</b>的别名（见该方法注释）。
    /// </summary>
    private const int FileLockStripes = 64;
    private static readonly SemaphoreSlim[] _fileLocks = BuildFileLocks();

    private static SemaphoreSlim[] BuildFileLocks()
    {
        SemaphoreSlim[] locks = new SemaphoreSlim[FileLockStripes];
        for (int i = 0; i < locks.Length; i++) locks[i] = new SemaphoreSlim(1, 1);
        return locks;
    }

    private static SemaphoreSlim LockFor(string path)
        => _fileLocks[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(path) % _fileLocks.Length];

    private readonly string _workspaceRoot;
    private readonly SimpleGlobber _glob;
    private readonly SimpleGrepper _grepper;
    private readonly IFileBackupStore _backupStore; //组合持有:Write 覆盖前备份用,可注入

    public PermissiveFileAccessTools(string workspaceRoot, IFileBackupStore? backupStore = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _glob = new SimpleGlobber(workspaceRoot);
        _grepper = new SimpleGrepper(workspaceRoot);
        _backupStore = backupStore ?? new FileBackupStore();
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
        [Description("Where to search: a directory (search under it, e.g. \"Design/spec\") "
                     + "or a single file (match only that file, e.g. \"Design/spec/04-世界层.md\"). "
                     + "Omit it to search the whole working directory. "
                     + "Relative or absolute. Returned paths are relative to the working directory either way, "
                     + "so you can pass them straight to `Read`.")]
        string? path = null)
    {
        GlobOutcome outcome = await _glob.SearchAsync(pattern, path).ConfigureAwait(false);

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
            return $"Searched \"{outcome.ResolvedDirectory}\" — 0 matches; the path exists, "
                   + "nothing matched the pattern.";
        }

        return outcome.Truncated
            ? $"Showing the first {outcome.Entries.Count} entries; more were dropped. "
              + "Narrow the pattern or scope it with path."
            : null;
    }

    [Description("Search file contents. Respects .gitignore. If hits are too many, returns a hit map instead of inline lines: then narrow with path/fileGlobs or Read the listed files — never re-search with a broader term.")]
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
                     + "(e.g. not \"**/*.cs\"); to scope the search use the 'path' argument. "
                     + "Ignored when path points to a single file. "
                     + "A leading '**/' or path in a glob is stripped to the bare file name.")]
        string[]? fileGlobs = null,
        [Description("Where to search: a directory (search under it, e.g. \"Design/spec\"), "
                     + "a single file (search only that file, e.g. \"Design/spec/04-世界层.md\"), "
                     + "or omit it to search the whole working directory. "
                     + "Relative or absolute. Returned paths are relative to the working directory either way, "
                     + "so you can pass them straight to `Read`.")]
        string? path = null,
        CancellationToken ct = default)
    {
        GrepOutcome outcome = await _grepper
            .SearchAsync(pattern, isRegex, caseSensitive, contextLines, maxDepth, fileGlobs, path, ct)
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
                            + "or narrow the query (fileGlobs/path) instead of re-searching with a broader term.";

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
                parts.Add($"Searched \"{outcome.ResolvedDirectory}\" — 0 matches; "
                          + "the path exists, nothing there matched.");
            }

            if (droppedMatches > 0)
            {
                parts.Add($"Showing the first {MaxGrepMatches} matches; {droppedMatches} more "
                          + $"across {droppedFiles.Count} file(s) were dropped. "
                          + "Narrow the query, or scope it with fileGlobs/path.");
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
                 - By default at most 2000 lines or 32KB are returned per call, whichever comes first;
                   a trailing notice tells you the offset to continue from.
                 - Pass limit=-1 to read the entire file in one call, bypassing the byte cap.
                   Use this when you need to understand the whole file for refactoring.
                 - When you only need part of a large file, locate the interesting lines with Grep first,
                   then read a slice with offset/limit — don't pull the whole file for a detail.
                 """)]
    internal Task<string> Read(
        [Description("File path, absolute or relative to the working directory.")] string filePath,
        [Description("1-based starting line.")] int offset = 1,
        [Description("Max lines to return. Pass -1 to read the entire file (bypasses the 32KB byte cap). " +
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
    internal async Task<string> Write(
        [Description("File path, absolute or relative to the working directory.")]
        string filePath,
        [Description("Full file content.")] string content,
        CancellationToken ct = default)
    {
        // 写目标解析 symlink:锁与落盘都对着真实路径(否则链接被替换成普通文件)
        string full = ResolveWriteTarget(ResolvePath(filePath));
        SemaphoreSlim fileLock = LockFor(full);
        await fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte[]? originalBytes = null; //已有文件才有,顺手读来做备份与信封
            if (File.Exists(full)) originalBytes = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);

            // 覆盖已有文件时沿用它的 BOM 与行尾风格;新建文件则是无 BOM + 模型给的 \n。
            // 不这么做的话,让模型重写一个 CRLF 文件会顺手把整份文件的行尾改掉
            TextFileEnvelope envelope = originalBytes is null
                ? TextFileEnvelope.FromText(string.Empty)
                : TextFileEnvelope.FromBytes(originalBytes);

            string? backupPath = null; //新建文件无备份
            if (originalBytes is not null)
                backupPath = await _backupStore.BackupAsync(full, originalBytes, ct).ConfigureAwait(false);

            await SaveAsync(full, envelope, envelope.ConvertNewLines(content), ct).ConfigureAwait(false);
            int lines = content.Split('\n').Length;
            return backupPath is null
                ? $"Saved '{filePath}' ({lines} lines)."
                : $"Saved '{filePath}' ({lines} lines). Previous version backed up to '{backupPath}'.";
        }
        finally
        {
            fileLock.Release();
        }
    }

    [Description("""
                 Change an existing file by exact text replacement.
                 - Put every change to one file in a single call, as multiple entries in `edits`.
                 - Every edits[].oldString is matched against the file as it is now, not against
                   your earlier entries in the same call, and must match exactly one place.
                 - Entries must not overlap. Merge nearby changes into one entry instead.
                 - Nothing is written unless every entry applies; the error tells you what to fix.
                 """)]
    internal async Task<string> Edit(
        [Description("File path, absolute or relative to the working directory.")]
        string filePath,
        [Description("The replacements to make, all matched against the current file content.")]
        List<FileEdit> edits,
        CancellationToken ct = default)
    {
        // 写目标解析 symlink:锁与落盘都对着真实路径(否则链接被替换成普通文件)
        string full = ResolveWriteTarget(ResolvePath(filePath));
        SemaphoreSlim fileLock = LockFor(full);
        await fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            FileEditPlan plan = await FileEditPlanner.PlanFileAsync(full, filePath, edits, ct).ConfigureAwait(false);
            if (!plan.Succeeded) return $"[Edit failed] {plan.Error}";

            await SaveAsync(full, plan.Envelope, plan.NewText, ct).ConfigureAwait(false);

            string diff = FileEditPlanner.RenderDiff(plan.Diff, MaxEditDiffLines, MaxEditDiffLineChars);
            return $"Applied {edits.Count} edit(s) to '{filePath}'.\n{diff}";
        }
        finally
        {
            fileLock.Release();
        }
    }

    //统一落盘:BOM 与行尾按信封原样还原。先写同目录临时文件再原子替换——
    // 读方永远看到旧或新完整内容(不会读到写一半的文件),进程中途崩溃也不留残缺目标文件;
    // 同目录是前提:跨文件系统 rename 会退化成 copy+delete,失去原子性。
    // 注意"原子"指替换动作本身:只保证读方不看到半截,不保证断电后数据在盘(无 fsync)。
    // 读方(Read/Grep/预览)故意不进锁——原子写让它们永远看到完整文件,不需要锁。
    // 固有代价:rename 替换目录项会<b>断开硬链接关系</b>(其它链接仍指向旧 inode、
    // 永远读到旧内容,不报错)——vim 式原子写的通病,非本仓回归,知道即可。
    private static async Task SaveAsync(string full, TextFileEnvelope envelope, string content, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temp = Path.Combine(Path.GetDirectoryName(full)!,
            $".{Path.GetFileName(full)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, envelope.ToBytes(content), ct).ConfigureAwait(false);

            // 原子替换会新建 inode,原文件的权限/可执行位必须搬到 temp 再换,
            // 否则编辑一个 755 的脚本后它变成 644(执行位静默丢失)。非 Unix 平台或
            // 权限复制失败都不阻断写入。
            if (File.Exists(full))
            {
                try
                {
                    if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                    {
                        File.SetUnixFileMode(temp, File.GetUnixFileMode(full));
                    }
                }
                catch
                {
                    // 权限读取失败:保留默认权限继续写
                }
            }

            try
            {
                File.Move(temp, full, overwrite: true);
            }
            catch (IOException) when (File.Exists(full))
            {
                // Windows:目标被其它进程打开(未开 FILE_SHARE_DELETE)时 Move 抛 IOException。
                // 回退直接写目标——失去原子性但保住可用性,模型拿到的仍是成功结果而不是裸异常。
                await File.WriteAllBytesAsync(full, envelope.ToBytes(content), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // 临时文件清理失败不影响主路径(目标已由 Move 决定)
            }
        }
    }

    // ---- 路径解析 ----

    private string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_workspaceRoot, path));
    }

    /// <summary>
    /// 写目标解析：跟随符号链接到真实路径。锁与原子写都对着<b>解析后的</b>路径做，
    /// 否则两个回归同时发生：<c>File.Move</c> 是 rename 覆盖链接<b>本体</b>（不写目标，
    /// 静默把链接换成普通文件），且 <c>/real</c> 与 <c>/link→real</c> 在锁表里拿不同条纹、
    /// 互斥失效。
    /// 解析失败（例如文件不存在）回退原路径。
    ///
    /// <b>已知边界</b>：只解析末段组件——父目录是 symlink（<c>/work/linkdir/target.txt</c>，
    /// <c>linkdir → realdir</c>）时返回的仍是 <c>/work/linkdir/target.txt</c>，与
    /// <c>/work/realdir/target.txt</c> 在锁表里是两条不同条纹（同一 inode），并发别名编辑
    /// 仍可能 lost-update。与 <c>ApprovalModeMapper</c>"不解析符号链接"口径一致，接受此边界；
    /// 全路径 realpath 会引入 TOCTOU 与锁 key 一致性问题，对代理场景不划算。
    /// </summary>
    private static string ResolveWriteTarget(string full)
    {
        try
        {
            FileSystemInfo? target = File.ResolveLinkTarget(full, returnFinalTarget: true);
            return target?.FullName ?? full;
        }
        catch
        {
            return full; // 解析失败（文件不存在等）回退原路径
        }
    }
}
