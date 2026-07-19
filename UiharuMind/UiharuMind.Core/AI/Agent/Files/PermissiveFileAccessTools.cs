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
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;

namespace UiharuMind.Core.AI.Agent.Files;

/// <summary>
/// 自带的 file_access_* 工具集,替代 MFA 内置的 FileAccessProvider。
/// MFA 的 FileAccessProvider 在调用存储前会用 StorePaths.NormalizeRelativePath 拒绝一切绝对路径,
/// 且该类为 internal 无法继承/修改;因此这里完全自行实现文件访问:
/// - 相对路径解析到工作区根目录;
/// - 绝对路径直接访问真实文件系统(需用户审批);
/// 仅做符号链接/重解析点防护避免越权。replace/replace_lines 逻辑通过 MfaFileEditor 反射转发到 MFA 内部 FileEditor。
/// 每个工具包一层 ApprovalRequiredAIFunction,沿用 MFA 的审批管线。
/// 工具方法说明直接复制 MFA FileAccessProvider 原文;参数上不附加描述(与 MFA 一致)。
/// </summary>
internal sealed class PermissiveFileAccessTools
{
    public const string ReadToolName = "Read";
    public const string WriteToolName = "Write";
    public const string DeleteToolName = "Delete";
    public const string LsToolName = "List";
    public const string GrepToolName = "Grep";
    public const string GlobToolName = "Glob";
    public const string ReplaceToolName = "Replace";
    public const string ReplaceLinesToolName = "ReplaceLines";

    private readonly string _workspaceRoot;

    public PermissiveFileAccessTools(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        Directory.CreateDirectory(_workspaceRoot);
    }

    public IReadOnlyList<AITool> Create(bool disableWriteTools = false)
    {
        var tools = new List<AITool>
        {
           AIFunctionFactory.Create(ReadImpl, new AIFunctionFactoryOptions { Name = ReadToolName }),
           AIFunctionFactory.Create(LsImpl, new AIFunctionFactoryOptions { Name = LsToolName }),
           AIFunctionFactory.Create(GrepImpl, new AIFunctionFactoryOptions { Name = GrepToolName }),
        };

        if (!disableWriteTools)
        {
            tools.Add(Wrap(AIFunctionFactory.Create(WriteImpl, new AIFunctionFactoryOptions { Name = WriteToolName })));
            tools.Add(Wrap(AIFunctionFactory.Create(DeleteImpl, new AIFunctionFactoryOptions { Name = DeleteToolName })));
            tools.Add(Wrap(AIFunctionFactory.Create(ReplaceImpl, new AIFunctionFactoryOptions { Name = ReplaceToolName })));
            tools.Add(Wrap(AIFunctionFactory.Create(ReplaceLinesImpl, new AIFunctionFactoryOptions { Name = ReplaceLinesToolName })));
        }

        return tools;

        static AITool Wrap(AIFunction function) => new ApprovalRequiredAIFunction(function);
    }

    [Description("Read the content of a file.")]
    private Task<string> ReadImpl(string filePath, CancellationToken cancellationToken = default)
    {
        string full = ResolvePath(filePath);
        return Task.FromResult(File.Exists(full) ? File.ReadAllText(full, Encoding.UTF8) : $"File '{filePath}' not found.");
    }

    [Description("列出指定目录文件")]
    private Task<List<FileStoreEntry>> LsImpl(string? directory = null, [Description("(e.g. \"*.md\")")]string? globPattern = null)
    {
        string target = string.IsNullOrWhiteSpace(directory) ? _workspaceRoot : ResolvePath(directory!);
        if (!Directory.Exists(target)) return Task.FromResult(new List<FileStoreEntry>());

        var entries = new List<FileStoreEntry>();
        foreach (string dir in Directory.GetDirectories(target))
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
            string? name = Path.GetFileName(dir);
            if (name is not null) entries.Add(new FileStoreEntry(name, FileStoreEntry.Directory));
        }

        foreach (string file in Directory.GetFiles(target))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            string? name = Path.GetFileName(file);
            if (name is not null) entries.Add(new FileStoreEntry(name, FileStoreEntry.File));
        }

        if (string.IsNullOrWhiteSpace(globPattern)) return Task.FromResult(entries);
        var regex = "^" + Regex.Escape(globPattern!).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Task.FromResult(entries.Where(e => Regex.IsMatch(e.Name, regex, RegexOptions.IgnoreCase)).ToList());
    }

    [Description("Write a file with the given name and content.")]
    private async Task<string> WriteImpl(string filePath, string content, bool overwrite = false, CancellationToken cancellationToken = default)
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

    [Description("Replace occurrences of old_string with new_string in a file.")]
    private async Task<string> ReplaceImpl(string filePath, string oldString, string newString, bool replaceAll = false, CancellationToken cancellationToken = default)
    {
        string full = ResolvePath(filePath);
        if (!File.Exists(full)) return $"File '{filePath}' not found.";
        string content = await File.ReadAllTextAsync(full, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        (string newContent, int count) = MfaFileEditor.ApplyReplace(content, oldString, newString, replaceAll);
        await File.WriteAllTextAsync(full, newContent, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return $"Replaced {count} occurrence(s) in '{filePath}'.";
    }

    [Description("Replace lines in a file. Provide a list of edits, each with a 1-based line_number and a literal new_line (include your own trailing newline); an empty new_line deletes the line, including its line break.")]
    private async Task<string> ReplaceLinesImpl(string fileName, List<FileLineEdit> edits, CancellationToken cancellationToken = default)
    {
        string full = ResolvePath(fileName);
        if (!File.Exists(full)) return $"File '{fileName}' not found.";
        string content = await File.ReadAllTextAsync(full, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        string newContent = MfaFileEditor.ApplyReplaceLines(content, edits);
        await File.WriteAllTextAsync(full, newContent, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return $"Replaced {edits.Count} line(s) in '{fileName}'.";
    }

    [Description(
        """
        Search the contents of files in the store (recursively, across all subdirectories) using a regular expression pattern (case-insensitive).
        Optionally restrict the search to a base directory (relative path), and filter which files to search using a glob pattern matched against each file's path relative to that directory:
        - '*' matches within a single path segment
        - '**' matches across subdirectories, so use \"**/*.md\" to match markdown files at any depth, or \"reports/**\" to restrict the search to the 'reports' subtree.

        Returns matching results whose file names are paths relative to the store root (usable with file_access_read), along with snippets and matching lines with line numbers.
        """)]
    private Task<List<FileSearchResult>> GrepImpl(string regexPattern, string? globPattern = null, string? directory = null)
    {
        string? pattern = string.IsNullOrWhiteSpace(globPattern) ? null : globPattern;
        string target = string.IsNullOrWhiteSpace(directory) ? _workspaceRoot : ResolvePath(directory!);
        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
            return Task.FromResult(new List<FileSearchResult>());

        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
        Matcher? matcher = string.IsNullOrWhiteSpace(pattern) ? null : new Matcher().AddInclude(pattern!);

        var results = new List<FileSearchResult>();
        foreach (string filePath in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0) continue;

            string relativeName = GetRelativePath(target, filePath);
            if (matcher is not null && !matcher.Match(relativeName).HasMatches) continue;

            string fileContent = File.ReadAllText(filePath, Encoding.UTF8);
            string[] lines = fileContent.Split('\n');
            var matchingLines = new List<FileSearchMatch>();
            string? firstSnippet = null;
            int lineStartOffset = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                Match match = regex.Match(lines[i]);
                if (match.Success)
                {
                    matchingLines.Add(new FileSearchMatch { LineNumber = i + 1, Line = lines[i].TrimEnd('\r') });
                    if (firstSnippet is null)
                    {
                        int charIndex = lineStartOffset + match.Index;
                        int snippetStart = Math.Max(0, charIndex - 50);
                        int snippetEnd = Math.Min(fileContent.Length, charIndex + match.Value.Length + 50);
                        firstSnippet = fileContent.Substring(snippetStart, snippetEnd - snippetStart);
                    }
                }

                lineStartOffset += lines[i].Length + 1;
            }

            if (matchingLines.Count > 0)
            {
                results.Add(new FileSearchResult
                {
                    FileName = relativeName,
                    Snippet = firstSnippet!,
                    MatchingLines = matchingLines,
                });
            }
        }

        return Task.FromResult(results);
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

    private static string GetRelativePath(string baseDirectory, string filePath)
    {
        string baseTrimmed = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string relative = filePath.Substring(baseTrimmed.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
