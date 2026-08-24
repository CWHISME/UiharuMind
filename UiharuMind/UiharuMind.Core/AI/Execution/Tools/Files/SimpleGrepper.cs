using System.Text.RegularExpressions;
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
        _rootDirectory = Path.GetFullPath(workspaceRoot);
    }

    /// <summary>
    /// 搜索文本。<b>失败与"搜到 0 条"分开返回</b>，见 <see cref="GrepOutcome"/>。
    ///
    /// 从前这里有两个坑，都是"骗模型"级别的：目录不存在直接 <c>return new()</c>，
    /// 与"这个词确实没有"完全无法区分——模型于是换个关键词在错目录里反复搜；
    /// 引擎异常则被包装成一条 <c>FileName = ""</c> 的假命中，模型看到的是"有 1 条结果"。
    ///
    /// <paramref name="isRegex"/> 为 true 时先做一步 glob 味归一化，编译不过再降级为字面串，
    /// 见 <see cref="NormalizeRegex"/>。
    /// </summary>
    /// <param name="query">要搜索的内容</param>
    /// <param name="isRegex">为 true 时把 query 当正则（编译不过会自动降级为字面量），否则按字面量</param>
    /// <param name="caseSensitive">是否区分大小写</param>
    /// <param name="contextLines">命中行上下各带几行上下文</param>
    /// <param name="maxDepth">目录遍历最大深度（null 不限制）</param>
    /// <param name="fileGlobs">按<b>文件名</b>（不含路径）过滤，如 <c>*.cs</c>；null/空则不过滤</param>
    /// <param name="directory">搜索根：绝对路径直接用，相对路径拼工作区</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>命中列表与失败原因</returns>
    public async Task<GrepOutcome> SearchAsync(
        string query,
        bool isRegex = false,
        bool caseSensitive = false,
        int contextLines = 0,
        int? maxDepth = null,
        string[]? fileGlobs = null,
        string? directory = null,
        CancellationToken ct = default)
    {
        string target = SearchRoot.Resolve(_rootDirectory, directory);

        if (!Directory.Exists(target))
        {
            return new GrepOutcome
            {
                ResolvedDirectory = target,
                EffectiveQuery = query,
                Failure = new SearchFailure
                {
                    Kind = ESearchFailureKind.DirectoryNotFound,
                    RequestedDirectory = directory,
                    ResolvedDirectory = target,
                    WorkingDirectory = _rootDirectory,
                    Pattern = query,
                },
            };
        }

        string effective = query;
        bool fellBack = false;
        if (isRegex)
        {
            effective = NormalizeRegex(query);
            if (!IsCompilableRegex(effective))
            {
                // 降级而不是报错:模型写的多半是想当通配符用的字面串,按字面搜正是它要的东西。
                // 但降级这件事必须回报给模型,否则它对"为什么少了几条命中"会推错
                effective = query;
                isRegex = false;
                fellBack = true;
            }
        }

        try
        {
            var engine = new SearchEngine(target);
            List<SearchResult> matches = await engine.SearchAsync(
                query: effective,
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
                    // 引擎回的是相对搜索根的路径,换算成相对工作区——否则缩了 directory 之后
                    // 回来的路径喂给 Read 会解析到别处(见 SearchRoot.ToPortablePath)
                    FileName = SearchRoot.ToPortablePath(_rootDirectory, Path.Combine(target, match.FilePath)),
                    Snippet = match.MatchContent?.TrimEnd() ?? "",
                    MatchingLines = BuildLines(match),
                });
            }

            return new GrepOutcome
            {
                Matches = results,
                ResolvedDirectory = target,
                FellBackToLiteral = fellBack,
                EffectiveQuery = effective,
            };
        }
        catch (OperationCanceledException)
        {
            throw; //取消是正常流程,由调用方处理,不该伪装成一次搜索失败
        }
        catch (Exception ex)
        {
            return new GrepOutcome
            {
                ResolvedDirectory = target,
                EffectiveQuery = effective,
                FellBackToLiteral = fellBack,
                Failure = new SearchFailure
                {
                    Kind = ESearchFailureKind.EngineFailed,
                    RequestedDirectory = directory,
                    ResolvedDirectory = target,
                    WorkingDirectory = _rootDirectory,
                    Pattern = effective,
                    Detail = ex.Message,
                },
            };
        }
    }

    /// <summary>
    /// glob 味归一化：把无从量化的前导 <c>*</c> 补成 <c>.*</c>。
    ///
    /// 实测模型偏爱写 <c>*Foo</c>、<c>*Foo*</c> 这种半 glob 半正则的东西，而它<b>两条路都不通</b>：
    /// 当正则非法（<c>Quantifier '*' following nothing</c>），当字面串也搜不到。
    /// 补成 <c>.*Foo</c> 才是它真正想表达的意思。
    ///
    /// <b>只动首尾这一处</b>：中间的 <c>*</c> 前面总有东西可量化，那是合法正则，
    /// 替换它反而会改掉一个本来写对了的表达式的语义。
    /// </summary>
    /// <param name="query">调用方给的表达式</param>
    /// <returns>归一化之后的表达式</returns>
    internal static string NormalizeRegex(string query)
    {
        if (string.IsNullOrEmpty(query)) return query;

        string result = query;
        if (result.StartsWith('*')) result = "." + result;
        // 尾随的 * 本身是合法的(前面有东西可量化);只有 "foo**" 这种连着两个才无从量化
        if (result.EndsWith("**")) result = result[..^1];
        return result;
    }

    private static bool IsCompilableRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
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
