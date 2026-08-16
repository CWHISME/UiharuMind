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
    /// </summary>
    internal const int MaxReadTotalBytes = 64 * 1024;

    internal const int MaxReadLineChars = 2000; //单行截断(压缩产物一行可达数百 KB)
    internal const int MaxGrepMatches = 200; //Grep 命中上限(只限工具边界,UI 文件搜索仍全量)
    internal const int MaxGrepLineChars = 500; //Grep 单行截断
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
            AIFunctionFactory.Create(Read, new AIFunctionFactoryOptions { Name = FileToolNames.Read }),
            AIFunctionFactory.Create(Glob, new AIFunctionFactoryOptions { Name = FileToolNames.Glob }),
            AIFunctionFactory.Create(Grep, new AIFunctionFactoryOptions { Name = FileToolNames.Grep }),
        };

        if (!disableWriteTools)
        {
            tools.Add(Wrap(AIFunctionFactory.Create(Write, new AIFunctionFactoryOptions { Name = FileToolNames.Write })));
            tools.Add(Wrap(AIFunctionFactory.Create(Edit, new AIFunctionFactoryOptions { Name = FileToolNames.Edit })));
        }

        return tools;

        static AITool Wrap(AIFunction function) => new ApprovalRequiredAIFunction(function);
    }

    [Description("Find files by glob pattern.")]
    private Task<List<string>> Glob(
        [Description("Glob pattern, e.g. \"**/*.cs\".")] string pattern,
        [Description("Directory to search in, absolute or relative to the working directory (optional).")]
        string? root = null)
        => _glob.SearchAsync(pattern, root);

    [Description("Search file contents. Respects .gitignore.")]
    internal async Task<List<FileSearchResult>> Grep(
        [Description("Search pattern (ripgrep syntax).")] string query,
        [Description("Treat the pattern as a regular expression.")] bool isRegex = true,
        [Description("Case-sensitive search.")] bool caseSensitive = false,
        [Description("How many lines of context to show around each match.")] int contextLines = 0,
        [Description("Maximum directory depth to walk (null means no limit).")] int? maxDepth = null,
        [Description("Only search files whose name matches one of these globs, e.g. \"*.cs\".")]
        string[]? fileGlobs = null,
        [Description("Directory to search in, absolute or relative to the working directory.")]
        string? directory = null,
        CancellationToken ct = default)
    {
        List<GrepMatchResult> results = await _grepper
            .SearchAsync(query, isRegex, caseSensitive, contextLines, maxDepth, fileGlobs, directory, ct)
            .ConfigureAwait(false);

        // 自有结果 → 框架工具结果的转换只发生在这里,按文件聚合与命中限幅也只发生在这里。
        // 聚合是为模型做的:同一文件十处命中摊成十条,文件名就要重复十遍,而模型真正需要的是
        // "哪个文件、第几行"。界面那侧要的是可逐条点开的命中列表,所以不在 grepper 里聚合。
        List<FileSearchResult> converted = [];
        Dictionary<string, FileSearchResult> byFile = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<int>> seenLines = new(StringComparer.Ordinal);
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

            remaining--;
            if (!byFile.TryGetValue(result.FileName, out FileSearchResult? file))
            {
                file = new FileSearchResult
                {
                    FileName = result.FileName,
                    Snippet = TruncateLine(result.Snippet, MaxGrepLineChars),
                    MatchingLines = [],
                };
                byFile[result.FileName] = file;
                seenLines[result.FileName] = [];
                converted.Add(file);
            }

            // 相邻命中的上下文会重叠,同一行只保留一次
            HashSet<int> seen = seenLines[result.FileName];
            foreach (GrepMatchLine line in result.MatchingLines)
            {
                if (!seen.Add(line.LineNumber)) continue;
                file.MatchingLines.Add(new FileSearchMatch
                {
                    LineNumber = line.LineNumber,
                    Line = TruncateLine(line.Line, MaxGrepLineChars),
                });
            }
        }

        foreach (FileSearchResult file in converted)
        {
            file.MatchingLines.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));
        }

        if (droppedMatches > 0)
        {
            converted.Add(new FileSearchResult
            {
                FileName = "[truncated]",
                Snippet = $"Showing the first {MaxGrepMatches} matches; {droppedMatches} more "
                          + $"across {droppedFiles.Count} file(s) were dropped. "
                          + "Narrow the query, or scope it with fileGlobs/directory.",
            });
        }

        return converted;
    }

    /// <summary>超长行截断(限幅只服务工具输出,不改动底层搜索结果)</summary>
    internal static string TruncateLine(string text, int maxChars)
    {
        return text.Length <= maxChars ? text : text[..maxChars] + " …[truncated]";
    }

    [Description("""
                 Read a file's raw content.
                 - Lines are separated by newlines. The first line of your mental model is line 1.
                 - At most 2000 lines or 64KB are returned per call, whichever comes first;
                   a trailing notice tells you the offset to continue from.
                 """)]
    internal Task<string> Read(
        [Description("File path, absolute or relative to the working directory.")] string filePath,
        [Description("1-based starting line.")] int offset = 1,
        [Description("Max lines to return (capped by the default window).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        string full = ResolvePath(filePath);
        if (!File.Exists(full)) return Task.FromResult($"File '{filePath}' not found.");

        if (offset < 1) offset = 1;
        // 上限从"模型自觉"改为强制:不传 limit 时套默认窗口,总量另设保险——
        // 编码会话一次误读大文件就是几万 token,截断必须由工具侧兜底
        int effectiveLimit = Math.Max(1, limit ?? DefaultReadLineLimit);

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
            if (lines.Count >= effectiveLimit || totalBytes >= MaxReadTotalBytes)
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
        return Task.FromResult(
            $"{content}\n…[truncated: showing lines {offset}–{nextOffset - 1}; continue with offset={nextOffset}]");
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
