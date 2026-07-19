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

namespace UiharuMind.Core.AI.Agent.Files;

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
    public const string DeleteToolName = "Delete";
    public const string GrepToolName = "Grep";
    public const string GlobToolName = "Glob";
    public const string EditToolName = "Edit";

    private readonly string _workspaceRoot;
    private readonly SimpleGlobber _glob;


    public PermissiveFileAccessTools(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _glob = new SimpleGlobber(workspaceRoot);
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
            tools.Add(Wrap(AIFunctionFactory.Create(WriteImpl, new AIFunctionFactoryOptions { Name = WriteToolName })));
            tools.Add(Wrap(AIFunctionFactory.Create(DeleteImpl, new AIFunctionFactoryOptions { Name = DeleteToolName })));
            tools.Add(Wrap(AIFunctionFactory.Create(EditImpl, new AIFunctionFactoryOptions { Name = EditToolName })));
        }

        return tools;

        static AITool Wrap(AIFunction function) => new ApprovalRequiredAIFunction(function);
    }

    [Description("Read the content of a file.")]
    private Task<string> Read(
        [Description("Path to the file, relative to the workspace root or an absolute path.")] string filePath,
        [Description("1-based line to start reading from. Use to skip to a region of a large file.")] int offset = 1,
        [Description("Maximum number of lines to return. Omit to read the rest of the file.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        string full = ResolvePath(filePath);
        if (!File.Exists(full)) return Task.FromResult($"File '{filePath}' not found.");

        if (offset < 1) offset = 1;
        var lines = new List<string>();
        using var reader = new StreamReader(full, Encoding.UTF8);
        for (int current = 1; current < offset; current++)
        {
            if (reader.ReadLine() is null) break;
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lines.Add(line);
            if (limit is not null && lines.Count >= limit) break;
        }

        if (lines.Count == 0) return Task.FromResult($"File '{filePath}' is empty or offset is beyond its end.");
        return Task.FromResult(string.Join('\n', lines));
    }

    [Description("Find files/folders by glob. Trailing '/' = directories only. Supports *, ?, [a-z], {a,b}, **.")]
    private Task<List<string>> Glob(
        [Description("Pattern, e.g. 'src/**/*.cs'")] string pattern,
        [Description("Absolute path or sub-folder (optional)")] string? root = null)
        => _glob.SearchAsync(pattern, root);

    [Description("Search file contents using a literal or regular expression pattern.")]
    private async Task<List<FileSearchResult>> Grep(
        [Description("The search query. Treated as a literal string unless is_regex is true.")] string pattern,
        [Description("Treat the pattern as a regular expression instead of a literal string.")] bool isRegex = false,
        [Description("Match case-sensitively. By default the search is case-insensitive.")] bool caseSensitive = false,
        [Description("Number of context lines to include before and after each match.")] int contextLines = 0,
        [Description("Glob patterns to restrict which files are searched, e.g. [\"*.cs\", \"*.md\"].")] string[]? fileGlobs = null,
        [Description("Base directory to search, relative to the workspace root. Omit to search the whole workspace.")] string? directory = null,
        CancellationToken cancellationToken = default)
    {
        string target = string.IsNullOrWhiteSpace(directory) ? _workspaceRoot : ResolvePath(directory!);
        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
            return new List<FileSearchResult>();

        List<SearchResult> matches;
        try
        {
            var engine = new SearchEngine(target);
            matches = await engine.SearchAsync(
                pattern,
                isRegex: isRegex,
                caseSensitive: caseSensitive,
                contextLines: contextLines,
                fileGlobs: fileGlobs).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new List<FileSearchResult> { new() { FileName = string.Empty, Snippet = $"Search failed: {ex.Message}" } };
        }

        var results = new List<FileSearchResult>();
        foreach (SearchResult match in matches)
        {
            var relativeName = Path.GetRelativePath(target, match.FilePath);
            var matchingLines = new List<FileSearchMatch>
            {
                new() { LineNumber = match.LineNumber, Line = match.MatchContent },
            };

            results.Add(new FileSearchResult
            {
                FileName = relativeName,
                Snippet = match.MatchContent,
                MatchingLines = matchingLines,
            });
        }

        return results;
    }

    [Description("Create or overwrite a file with the given content.")]
    private async Task<string> WriteImpl(
        string filePath,
        string content,
        [Description("If false (default) and the file already exists, the write is rejected to avoid accidental overwrite.")] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        string full = ResolvePath(filePath);
        if (!overwrite && File.Exists(full))
            return $"File '{filePath}' already exists. To replace it, write again with overwrite set to true.";
        string? parent = Path.GetDirectoryName(full);
        if (parent is not null) Directory.CreateDirectory(parent);
        await File.WriteAllTextAsync(full, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return $"File '{filePath}' written.";
    }

    [Description("Delete a file.")]
    private Task<string> DeleteImpl(string filePath)
    {
        string full = ResolvePath(filePath);
        if (!File.Exists(full)) return Task.FromResult($"File '{filePath}' not found.");
        File.Delete(full);
        return Task.FromResult($"File '{filePath}' deleted.");
    }

    [Description("Edit a file via line-level edits or a string replacement.")]
    private async Task<string> EditImpl(
        [Description("Path to the file, relative to the workspace root or an absolute path.")] string filePath,
        [Description("Text to find and replace. Required when line_edits is not provided. Must appear exactly once unless replace_all is true.")] string? oldString = null,
        [Description("Replacement text for old_string. Defaults to empty (i.e. delete the matched text).")] string? newString = null,
        [Description("Replace every occurrence of old_string instead of requiring a single match.")] bool replaceAll = false,
        [Description("Line-level edits; each has a 1-based line_number and a literal new_line (include your own trailing newline); an empty new_line deletes the line. Takes precedence over old_string/new_string when provided.")] List<FileLineEdit>? lineEdits = null,
        CancellationToken cancellationToken = default)
    {
        string full = ResolvePath(filePath);
        if (!File.Exists(full)) return $"File '{filePath}' not found.";
        string content = await File.ReadAllTextAsync(full, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        string newContent;
        string message;
        if (lineEdits is { Count: > 0 })
        {
            newContent = MfaFileEditor.ApplyReplaceLines(content, lineEdits);
            message = $"Replaced {lineEdits.Count} line(s) in '{filePath}'.";
        }
        else if (!string.IsNullOrEmpty(oldString))
        {
            (newContent, int count) = MfaFileEditor.ApplyReplace(content, oldString!, newString ?? string.Empty, replaceAll);
            message = $"Replaced {count} occurrence(s) in '{filePath}'.";
        }
        else
        {
            return "Error: provide either line_edits or old_string (with optional new_string).";
        }

        await File.WriteAllTextAsync(full, newContent, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return message;
    }

    // ---- 路径解析与符号链接防护 ----

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            string full = Path.GetFullPath(path);
            ThrowIfContainsSymlink(full);
            return full;
        }

        string combined = Path.GetFullPath(Path.Combine(_workspaceRoot, path));
        ThrowIfContainsSymlink(combined);
        return combined;
    }

    private static void ThrowIfContainsSymlink(string fullPath)
    {
        var stack = new Stack<string>();
        string? seg = fullPath;
        while (!string.IsNullOrEmpty(seg) && seg != Path.GetPathRoot(seg))
        {
            stack.Push(seg);
            seg = Path.GetDirectoryName(seg);
        }

        foreach (string dir in stack)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(dir);
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException("Invalid path: the resolved path contains a symbolic link or reparse point.");
            }
        }
    }
}
