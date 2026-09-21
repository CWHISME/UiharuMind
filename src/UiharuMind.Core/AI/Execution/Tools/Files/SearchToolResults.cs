/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 两个搜索工具回给模型的形状：命中与<b>说明</b>分成两个字段。
///
/// 从前说明是混在命中列表里的假条目（<c>"[Error] ..."</c>、<c>FileName = "[truncated]"</c>），
/// 模型于是分不清"这是一条命中"还是"这是一句话"——最糟的一种是引擎异常被包成假命中，
/// 模型看到的是"有 1 条结果"。分成两个字段之后，"0 条命中"与"没搜成"在结构上就不同了。
/// </summary>
public sealed class GlobToolResult
{
    /// <summary>命中条目；没搜成时为空</summary>
    public List<string> Entries { get; set; } = [];

    /// <summary>给模型的说明：失败原因、0 命中、被截断。没有要说的就是 null</summary>
    public string? Notice { get; set; }
}

/// <summary>
/// 一个文件的一组命中行。
///
/// 按文件分组让模型一眼数出「几个文件、各几处」，路径只出现一次，token 远省于铺平的一行一命中
/// （同一文件命中越多越省）。行内容保持 grep 味：命中行 <c>行号: 内容</c>，上下文行 <c>行号- 内容</c>，
/// 行号拿来发 <c>Read offset=</c> 依旧毫不费力。
/// </summary>
public sealed class GrepFileHits
{
    /// <summary>文件路径（相对工作区）</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>命中行与其上下文，按行号升序</summary>
    public List<string> Lines { get; set; } = [];
}

/// <summary>
/// 地图模式下的一个条目：告诉模型「被丢掉的命中分布在哪」。
///
/// 命中过多时正文会被整页换成这张地图——半截正文是按扫描序取的、没有相关度排序，
/// 留着只会让模型锚定到运气好的文件上；地图则让模型直接对「哪些文件命中多」下判断：
/// 定点 <c>Read</c>，或意识到这个词搜宽了、换更窄的关键词。
/// </summary>
public sealed class GrepMapEntry
{
    /// <summary>文件路径（相对工作区）</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>该文件命中处数（一处命中连同其上下文行算一条）</summary>
    public int Hits { get; set; }

    /// <summary>命中行号区间 [FirstLine, LastLine]，拿来发 <c>Read offset=</c> 正好</summary>
    public int FirstLine { get; set; }

    /// <summary>命中行号区间上界</summary>
    public int LastLine { get; set; }

    /// <summary>首条命中片段（预览用，已按行截断）</summary>
    public string Snippet { get; set; } = string.Empty;
}

/// <summary>文本搜索工具回给模型的形状，与 <see cref="GlobToolResult"/> 同构</summary>
public sealed class GrepToolResult
{
    /// <summary>按文件分组的命中；没搜成时为空。地图模式下也为空（正文被整页换掉）</summary>
    public List<GrepFileHits> Matches { get; set; } = [];

    /// <summary>地图模式：命中过多时给的每文件命中分布，供定点 Read；正文模式为 null</summary>
    public List<GrepMapEntry>? Map { get; set; }

    /// <summary>给模型的说明：失败原因、自动降级、0 命中、被截断。没有要说的就是 null</summary>
    public string? Notice { get; set; }
}

/// <summary>
/// 把结构化的失败原因渲染成给<b>模型</b>看的一段话。
///
/// 措辞刻意用英文：它和工具 schema（<c>[Description]</c>）是同一层、紧挨着被模型读到，
/// 而 schema 保持英文（提示词散文才中文，见 ADR 0017）。
///
/// 每一句里的路径都是<b>当次真实的路径</b>，不是占位符。这是有意的：
/// 提示词里写通用示例会被小模型照抄（实机见过它编出 <c>root: "/path/to/project"</c>），
/// 而这里的"示例"由真实的工作目录与真实的入参拼成，没有占位符可抄；
/// 而且它只在出错那一次出现，常驻成本为零——这正是不把调用示例写进系统提示的原因。
/// </summary>
internal static class SearchFailureRenderer
{
    /// <summary>
    /// 渲染失败原因
    /// </summary>
    /// <param name="failure">结构化失败原因</param>
    /// <param name="literalToolName">"没有通配符"时该改用的字面查找工具名</param>
    /// <returns>给模型的说明文本</returns>
    public static string Render(SearchFailure failure, string literalToolName)
    {
        return failure.Kind switch
        {
            ESearchFailureKind.PathNotFound => PathNotFound(failure),
            ESearchFailureKind.InvalidGlobPattern =>
                $"Invalid glob pattern \"{failure.Pattern}\": {failure.Detail} "
                + "Patterns look like \"**/*.cs\" or \"src/**/Foo*\".",
            ESearchFailureKind.GlobHasNoWildcard =>
                $"\"{failure.Pattern}\" has no wildcard, so there is nothing to glob, "
                + $"and no file at that path under \"{failure.ResolvedDirectory}\". "
                + $"To read a known path use `{FileToolNames.Read}`; "
                + $"to find text inside files use `{literalToolName}`; "
                + "to glob, put a wildcard in the pattern, e.g. \"**/"
                + $"{Path.GetFileName(failure.Pattern)}\".",
            ESearchFailureKind.EngineFailed =>
                $"The search engine failed on \"{failure.Pattern}\": {failure.Detail}",
            _ => failure.Detail,
        };
    }

    /// <summary>
    /// 搜索范围（目录或单文件）不存在。<b>必须回显解析后的绝对路径</b>：模型看不到自己那个相对路径
    /// 被拼成了哪里，就只能再猜一次——这正是"试几次才找到正确用法"的机制。
    /// </summary>
    private static string PathNotFound(SearchFailure failure)
    {
        if (string.IsNullOrWhiteSpace(failure.RequestedDirectory))
        {
            return $"The working directory \"{failure.WorkingDirectory}\" does not exist.";
        }

        // 有最近存活祖先就点一句：让模型从"整个路径都不对"收敛到"断在某一段"，不猜候选。
        // 不再回显工作目录：它在系统提示里已写明（BuildWorkingDirectory），resolved 又已隐含拼接基准。
        string ancestor = string.IsNullOrEmpty(failure.NearestExistingDirectory)
            ? string.Empty
            : $" The nearest existing ancestor is \"{failure.NearestExistingDirectory}\".";

        return $"Path not found. You passed path \"{failure.RequestedDirectory}\", "
               + $"which resolves to \"{failure.ResolvedDirectory}\"."
               + ancestor
               + " Pass a directory (search under it), a single file (search only that file), "
               + "or omit path to search the whole working directory.";
    }
}
