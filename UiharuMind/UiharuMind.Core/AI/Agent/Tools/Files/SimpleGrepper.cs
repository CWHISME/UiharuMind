using System.IO.Enumeration;
using System.Text.RegularExpressions;
using Glacier.Grep;
using Meziantou.Framework.Globbing;

namespace UiharuMind.Core.AI.Agent.Files;

/// <summary>
/// 一条命中的行
/// </summary>
public sealed class GrepMatchLine
{
    /// <summary>
    /// 行号
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// 该行内容
    /// </summary>
    public string Line { get; set; } = string.Empty;
}

/// <summary>
/// 一个文件的搜索结果。
/// 项目自有类型：本类型也服务于界面的文件搜索功能，不应绑定 Agent Framework 的工具结果形状，
/// 框架形状的转换只发生在工具边界（PermissiveFileAccessTools）。
/// </summary>
public sealed class GrepFileResult
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 命中片段（用于预览）
    /// </summary>
    public string Snippet { get; set; } = string.Empty;

    /// <summary>
    /// 命中的各行
    /// </summary>
    public List<GrepMatchLine> MatchingLines { get; set; } = [];
}

/// <summary>
/// 文本搜索：标准 ripgrep 语法
/// </summary>
public sealed class SimpleGrepper
{
    private string _rootDirectory;

    public SimpleGrepper(string workspaceRoot)
    {
        _rootDirectory = workspaceRoot;
    }

    public async Task<List<GrepFileResult>> SearchAsync(
        string query,
        bool isRegex = false,
        bool caseSensitive = false,
        int contextLines = 0,
        int? maxDepth = null,
        string[]? fileGlobs = null,
        string? directory = null,
        CancellationToken ct = default)
    {
        // 外部绝对路径直接当搜索根，否则拼 workspace
        string target = string.IsNullOrWhiteSpace(directory)
            ? _rootDirectory
            : Path.IsPathFullyQualified(directory)
                ? Path.GetFullPath(directory)
                : Path.GetFullPath(Path.Combine(_rootDirectory, directory));

        if (!Directory.Exists(target))
            return new();

        try
        {
            /*- query：要搜索的内容（字符串）。
            - isRegex：为 true 时把 query 当作正则表达式匹配（否则按字面量匹配）。
            - caseSensitive：是否区分大小写。
            - contextLines：匹配行上下各显示多少个上下文行。
            - fileGlobs：按文件名（不含路径）过滤要搜索的文件，用 FileSystemName.MatchesSimpleExpression 做简单通配匹配（如 *.cs、*.gguf）。为 null/空则不过滤（见 SearchEngine.cs:63-76）。注意它只匹配文件名，不是路径或内容。
            - searchHidden：为 true 时包含隐藏文件/目录。
            - searchBinary：为 false 时跳过二进制文件（测试 gguf 目前漏检）。
            - invertMatch：反向匹配，输出不含 query 的行。
            - maxDepth：目录遍历最大深度（null 表示不限制）。
            注：fileGlobs 只按文件名通配过滤，且匹配的是 Path.GetFileName(fileTask.FullPath)（SearchEngine.cs:66），不是相对路径或扩展名以外的规则。
            */
            var engine = new SearchEngine(target);
            var matches = await engine.SearchAsync(
                query: query,
                isRegex: isRegex,
                caseSensitive: caseSensitive,
                contextLines: contextLines,
                maxDepth: maxDepth,
                fileGlobs: fileGlobs ?? Array.Empty<string>());

            var results = new List<GrepFileResult>();
            foreach (var match in matches)
            {
                results.Add(new GrepFileResult
                {
                    FileName = match.FilePath,
                    Snippet = match.MatchContent?.TrimEnd() ?? "",
                    MatchingLines = new List<GrepMatchLine>
                    {
                        new() { LineNumber = match.LineNumber, Line = match.MatchContent?.TrimEnd() ?? "" }
                    }
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            return new() { new() { FileName = "", Snippet = $"Search failed: {ex.Message}" } };
        }
    }
}