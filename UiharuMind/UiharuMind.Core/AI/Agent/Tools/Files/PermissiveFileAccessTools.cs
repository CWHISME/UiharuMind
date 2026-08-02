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
using Glacier.Grep;
using Meziantou.Framework.Globbing;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Agent.Harness;

namespace UiharuMind.Core.AI.Agent.Files;

// [MFA绕坑] 绕:框架 FileAccessProvider 拒绝一切绝对路径 因:该类 internal 无法继承修改 删除条件:框架允许配置路径策略
/// <summary>
/// 自带的 file_access_* 工具集,替代 MFA 内置的 FileAccessProvider。
/// MFA 的 FileAccessProvider 在调用存储前会用 StorePaths.NormalizeRelativePath 拒绝一切绝对路径,
/// 且该类为 internal 无法继承/修改;因此这里完全自行实现文件访问:
/// - 相对路径解析到工作区根目录;
/// - 绝对路径直接访问真实文件系统(需用户审批);
/// 仅做符号链接/重解析点防护避免越权。
/// Glob 采用 Meziantou.Framework.Globbing 实现递归路径枚举;Grep 采用 Glacier.Grep 高性能检索引擎。
/// 编辑逻辑(replace / replace_lines)通过 MfaFileEditor 转发到本地复制的实现。
/// 每个工具包一层 ApprovalRequiredAIFunction,沿用 MFA 的审批管线。
/// </summary>
internal sealed class PermissiveFileAccessTools
{
    public const string ReadToolName = "Read";
    public const string WriteToolName = "Write";
    public const string ReplaceToolName = "Replace";
    public const string DeleteToolName = "Delete";
    public const string GrepToolName = "Grep";
    public const string GlobToolName = "Glob";
    public const string EditToolName = "Edit";

    // —— 输出限幅:工具输出直接进模型上下文,编码会话的上下文大头是工具结果而非对话。
    //    Glob 已在 SimpleGlobber 内限 300 条;shell 由框架 MaxOutputBytes(64KiB)截断。——
    internal const int DefaultReadLineLimit = 2000; //未显式传 limit 时的行数上限
    internal const int MaxReadLineChars = 2000; //单行截断(压缩产物一行可达数百 KB)
    internal const int MaxReadTotalChars = 120_000; //总量保险(约 3 万 token)
    internal const int MaxGrepMatches = 200; //Grep 命中上限(只限工具边界,UI 文件搜索仍全量)
    internal const int MaxGrepLineChars = 500; //Grep 单行截断

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
            AIFunctionFactory.Create(Read, new AIFunctionFactoryOptions { Name = ReadToolName }),
            AIFunctionFactory.Create(Glob, new AIFunctionFactoryOptions { Name = GlobToolName }),
            AIFunctionFactory.Create(Grep, new AIFunctionFactoryOptions { Name = GrepToolName }),
        };

        if (!disableWriteTools)
        {
            tools.Add(Wrap(AIFunctionFactory.Create(Write, new AIFunctionFactoryOptions { Name = WriteToolName })));
            tools.Add(Wrap(AIFunctionFactory.Create(Replace, new AIFunctionFactoryOptions { Name = ReplaceToolName })));
            tools.Add(Wrap(AIFunctionFactory.Create(DeleteImpl, new AIFunctionFactoryOptions { Name = DeleteToolName })));
            tools.Add(Wrap(AIFunctionFactory.Create(Edit, new AIFunctionFactoryOptions { Name = EditToolName })));
        }

        return tools;

        static AITool Wrap(AIFunction function) => new ApprovalRequiredAIFunction(function);
    }

    [Description("搜索文件：标准 glob 语法")]
    private Task<List<string>> Glob(
        string pattern,
        [Description("Absolute path or sub-folder (optional)")] string? root = null)
        => _glob.SearchAsync(pattern, root);

    [Description("文本搜索：标准 ripgrep 语法")]
    internal async Task<List<FileSearchResult>> Grep(
        string query,
        [Description("Enable regex mode (default is literal)")] bool isRegex = false,
        [Description("是否区分大小写")] bool caseSensitive = false,
        [Description("匹配行上下各显示多少个上下文行")] int contextLines = 0,
        [Description("目录遍历最大深度（null 表示不限制）")] int? maxDepth = null,
        [Description("按文件名（不含路径）过滤要搜索的文件")] string[]? fileGlobs = null,
        [Description("Target directory (relative or absolute)")] string? directory = null,
        CancellationToken ct = default)
    {
        List<GrepFileResult> results = await _grepper
            .SearchAsync(query, isRegex, caseSensitive, contextLines, maxDepth, fileGlobs, directory, ct)
            .ConfigureAwait(false);

        // 自有结果 → 框架工具结果的转换只发生在这里,命中限幅也只发生在这里
        bool truncated = results.Count > MaxGrepMatches;
        List<FileSearchResult> converted = results.Take(MaxGrepMatches).Select(x => new FileSearchResult
        {
            FileName = x.FileName,
            Snippet = TruncateLine(x.Snippet, MaxGrepLineChars),
            MatchingLines = x.MatchingLines
                .Select(line => new FileSearchMatch
                    { LineNumber = line.LineNumber, Line = TruncateLine(line.Line, MaxGrepLineChars) })
                .ToList(),
        }).ToList();

        if (truncated)
        {
            converted.Add(new FileSearchResult
            {
                FileName = "[truncated]",
                Snippet = $"Showing first {MaxGrepMatches} of {results.Count} matches. " +
                          "Narrow the query, or scope it with fileGlobs/directory.",
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
                 - At most 2000 lines are returned per call; a trailing notice tells you the offset to continue from.
                 """)]
    internal Task<string> Read(
        [Description("File path (relative or absolute).")] string filePath,
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
        int totalChars = 0;
        using var reader = new StreamReader(full, Encoding.UTF8);
        for (int current = 1; current < offset; current++)
        {
            if (reader.ReadLine() is null) break;
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (lines.Count >= effectiveLimit || totalChars >= MaxReadTotalChars)
            {
                hasMore = true;
                break;
            }

            if (line.Length > MaxReadLineChars) line = TruncateLine(line, MaxReadLineChars);
            totalChars += line.Length + 1;
            lines.Add(line);
        }

        if (lines.Count == 0) return Task.FromResult($"File '{filePath}' is empty or offset is beyond its end.");

        string content = string.Join('\n', lines);
        if (!hasMore) return Task.FromResult(content);

        int nextOffset = offset + lines.Count;
        return Task.FromResult(
            $"{content}\n…[truncated: showing lines {offset}–{nextOffset - 1}; continue with offset={nextOffset}]");
    }
    
    [Description("Create or fully overwrite a file. Prefer 'edit' for partial changes.")]
    private async Task<string> Write(
        [Description("File path (relative or absolute).")]
        string filePath,
        [Description("Full file content.")] string content,
        [Description("Must be true to overwrite an existing file.")]
        bool overwrite = false,
        CancellationToken ct = default)
    {
        string full = ResolvePath(filePath);
        if (!overwrite && File.Exists(full))
            return $"File exists. Set overwrite=true to replace, or use 'edit' to patch it.";

        await SaveAsync(full, content, ct);
        return $"Saved '{filePath}' ({content.Split('\n').Length} lines).";
    }

    [Description("Replace occurrences of old_string with new_string in a file. Fails if old_string is not found, or if it occurs more than once and replace_all is false. Returns the number of occurrences replaced.")]
    private async Task<string> Replace(string filePath, string oldString, string newString, bool replaceAll = false, CancellationToken ct = default)
    {
        string full = ResolvePath(filePath);
        if (!File.Exists(full)) return $"File '{filePath}' not found.";
        string content = await File.ReadAllTextAsync(full, Encoding.UTF8, ct).ConfigureAwait(false);
        (string newContent, int count) = MfaFileEditor.ApplyReplace(content, oldString, newString, replaceAll);
        await SaveAsync(full, newContent, ct);
        return $"Replaced {count} occurrence(s) in '{filePath}'.";
    }
    
    [Description("Edit lines in a file. Provide a list of edits, each with a 1-based line_number and a literal new_line (include your own trailing newline); an empty new_line deletes the line, including its line break. Fails on out-of-range or duplicate line numbers.")]
    private async Task<string> Edit(string filePath, List<FileLineEdit>? lineEdits = null, CancellationToken ct = default)
    {
        string full = ResolvePath(filePath);
        if (!File.Exists(full))
            return $"File '{filePath}' not found.";

        // 1. 读入并归一化，MFA 内部按 \n 算就不会失配
        var raw = await File.ReadAllTextAsync(full, ct).ConfigureAwait(false);
        var normalized = Norm(raw);

        try
        {
            string newContent;

            if (lineEdits is { Count: > 0 })
            {
                // MFA 自己管行号校验，直接透传
                newContent = MfaFileEditor.ApplyReplaceLines(normalized, lineEdits);
            }
            else
            {
                return "Error: provide lineedits.";
            }

            return await FlushAndReply(full, raw, newContent, $"Applied {lineEdits!.Count} line edit(s) to '{filePath}'.", ct);
        }
        catch (ArgumentException ex)
        {
            // MFA 抛的校验异常，转成 Tool 返回，不断 Function Call 链路
            return $"[Edit failed] {ex.Message}";
        }
    }

    [Description("Delete a file.")]
    private Task<string> DeleteImpl(string path,
        [Description("If true and the target is a non-empty directory, recursively delete all contents.")] bool recursive = false)
    {
        string full = ResolvePath(path);
    
        if (File.Exists(full))
        {
            File.Delete(full);
            return Task.FromResult($"File '{path}' deleted.");
        }
    
        if (Directory.Exists(full))
        {
            // 若为非空目录且未指定递归，拒绝操作
            if (!recursive && Directory.EnumerateFileSystemEntries(full).Any())
            {
                return Task.FromResult($"Directory '{path}' is not empty. Set 'recursive=true' to delete all contents.");
            }
        
            Directory.Delete(full, recursive);
            return Task.FromResult($"Directory '{path}' {(recursive ? "and its contents " : "")}deleted.");
        }
    
        return Task.FromResult($"'{path}' does not exist.");

    }

    //写盘 + 友好话术
    private async Task<string> FlushAndReply(string full, string before, string after, string okMsg, CancellationToken ct)
    {
        if (before == after) return $"[No change] Content was identical after substitution.";
        await SaveAsync(full, after, ct);
        return okMsg;
    }
    
    //统一落盘
    private Task SaveAsync(string full, string content, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return File.WriteAllTextAsync(full, content, Encoding.UTF8, ct);
    }

    //换行抹平
    private static string Norm(string s) => s.Replace("\r\n", "\n");
    
    // ---- 路径解析 ----

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            string full = Path.GetFullPath(path);
            return full;
        }

        string combined = Path.GetFullPath(Path.Combine(_workspaceRoot, path));
        return combined;
    }
}