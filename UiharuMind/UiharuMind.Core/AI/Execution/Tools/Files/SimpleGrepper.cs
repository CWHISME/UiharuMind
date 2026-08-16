using Glacier.Grep;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 一条搜索结果里的行：命中行或它的上下文行
/// </summary>
public sealed class GrepMatchLine
{
    /// <summary>行号</summary>
    public int LineNumber { get; set; }

    /// <summary>该行内容</summary>
    public string Line { get; set; } = string.Empty;

    /// <summary>是命中行还是上下文行（上下文行不计入命中数限幅）</summary>
    public bool IsMatch { get; set; }
}

/// <summary>
/// <b>一处命中</b>及其上下文行。
///
/// 名字曾是 <c>GrepFileResult</c>、注释写"一个文件的搜索结果"，而实现一直是一个命中一条——
/// 界面的文件搜索窗正是按"一行一个命中"渲染的（<c>SearchService</c> 取 <c>MatchingLines.First()</c>）。
/// 按文件聚合只发生在工具边界（<c>PermissiveFileAccessTools.Grep</c>）：模型要的是紧凑的按文件视图，
/// 界面要的是可逐条点开的命中列表，两种形状各取所需，不在这一层强行统一。
///
/// 项目自有类型：不绑定 Agent Framework 的工具结果形状。
/// </summary>
public sealed class GrepMatchResult
{
    /// <summary>文件路径</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>命中片段（用于预览）</summary>
    public string Snippet { get; set; } = string.Empty;

    /// <summary>命中行与其上下文行，按行号升序</summary>
    public List<GrepMatchLine> MatchingLines { get; set; } = [];
}

/// <summary>
/// 文本搜索：标准 ripgrep 语法。
/// 引擎是进程内的 <see cref="SearchEngine"/>（Glacier.Grep：SIMD 热路径、工作窃取目录扫描，
/// 自带 <c>.gitignore</c>/<c>.ignore</c>/<c>.rgignore</c> 的层级排除，对齐 ripgrep 的优先级行为），
/// 因此这里不需要也不应该去外挂 rg 二进制。
/// </summary>
public sealed class SimpleGrepper
{
    private string _rootDirectory;

    public SimpleGrepper(string workspaceRoot)
    {
        _rootDirectory = workspaceRoot;
    }

    /// <summary>
    /// 搜索文本
    /// </summary>
    /// <param name="query">要搜索的内容</param>
    /// <param name="isRegex">为 true 时把 query 当正则，否则按字面量</param>
    /// <param name="caseSensitive">是否区分大小写</param>
    /// <param name="contextLines">命中行上下各带几行上下文</param>
    /// <param name="maxDepth">目录遍历最大深度（null 不限制）</param>
    /// <param name="fileGlobs">按<b>文件名</b>（不含路径）过滤，如 <c>*.cs</c>；null/空则不过滤</param>
    /// <param name="directory">搜索根：绝对路径直接用，相对路径拼工作区</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>命中列表，每处命中一条</returns>
    public async Task<List<GrepMatchResult>> SearchAsync(
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
            var engine = new SearchEngine(target);
            List<SearchResult> matches = await engine.SearchAsync(
                query: query,
                isRegex: isRegex,
                caseSensitive: caseSensitive,
                contextLines: contextLines,
                maxDepth: maxDepth,
                fileGlobs: fileGlobs ?? Array.Empty<string>()).ConfigureAwait(false);

            var results = new List<GrepMatchResult>(matches.Count);
            foreach (SearchResult match in matches)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(new GrepMatchResult
                {
                    FileName = match.FilePath,
                    Snippet = match.MatchContent?.TrimEnd() ?? "",
                    MatchingLines = BuildLines(match),
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            return new() { new() { FileName = "", Snippet = $"Search failed: {ex.Message}" } };
        }
    }

    /// <summary>
    /// 命中行 + 上下文行摊平成带行号的序列。
    /// 上下文行的行号由命中行推算：<c>ContextBefore</c> 紧贴命中行之前、<c>ContextAfter</c> 紧贴其后，
    /// 文件头尾处引擎给的条数会变少，减法依然成立。
    ///
    /// 这一步曾整个缺失：<c>contextLines</c> 一路传进了引擎，转换时却只取 <c>MatchContent</c>，
    /// 于是模型要了上下文、拿回来的还是孤零零一行——参数是假的。
    /// </summary>
    private static List<GrepMatchLine> BuildLines(SearchResult match)
    {
        List<string> before = match.ContextBefore ?? [];
        List<string> after = match.ContextAfter ?? [];
        var lines = new List<GrepMatchLine>(before.Count + after.Count + 1);

        for (int i = 0; i < before.Count; i++)
        {
            lines.Add(new GrepMatchLine
            {
                LineNumber = match.LineNumber - before.Count + i,
                Line = before[i].TrimEnd(),
            });
        }

        lines.Add(new GrepMatchLine
        {
            LineNumber = match.LineNumber,
            Line = match.MatchContent?.TrimEnd() ?? "",
            IsMatch = true,
        });

        for (int i = 0; i < after.Count; i++)
        {
            lines.Add(new GrepMatchLine
            {
                LineNumber = match.LineNumber + 1 + i,
                Line = after[i].TrimEnd(),
            });
        }

        return lines;
    }
}
