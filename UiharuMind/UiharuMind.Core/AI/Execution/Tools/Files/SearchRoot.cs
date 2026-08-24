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
/// 搜索根的解析规则。<b>两个搜索器共用一份</b>——它们从前各写一遍同样的三目表达式，
/// 而"相对路径拼谁"这件事一旦两边不一致，表现是同一个参数在 Glob 和 Grep 里指向不同目录。
/// </summary>
public static class SearchRoot
{
    /// <summary>
    /// 解析搜索根：没传就是工作区根，绝对路径直接用，相对路径拼工作区根。
    /// </summary>
    /// <param name="workingDirectory">工作区根目录</param>
    /// <param name="directory">调用方给的目录，可为 null/空</param>
    /// <returns>绝对路径</returns>
    public static string Resolve(string workingDirectory, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return Path.GetFullPath(workingDirectory);

        return Path.IsPathFullyQualified(directory)
            ? Path.GetFullPath(directory)
            : Path.GetFullPath(Path.Combine(workingDirectory, directory));
    }

    /// <summary>
    /// 搜索结果里一条路径该<b>怎么写给调用方</b>：能相对工作区就相对，否则给绝对路径。
    ///
    /// 基准必须是<b>工作区根</b>，不是本次的搜索根。从前两个搜索器都按搜索根算相对路径，
    /// 于是 <c>Grep(directory: "Core")</c> 回来的 <c>AI/Foo.cs</c> 喂给 <c>Read</c> 会解析到
    /// <c>&lt;工作区&gt;/AI/Foo.cs</c> —— 找不到。模型吃过几次之后就只信绝对路径了，
    /// 而那是它<b>理性</b>的选择：绝对路径是当时唯一跨工具通用的形式。
    /// 改成按工作区根算，搜索结果才第一次可以直接当 <c>Read</c> 的入参。
    ///
    /// 搜索根在工作区之外时不硬凑相对路径（那会得到一串 <c>../../</c>），直接给绝对路径。
    /// </summary>
    /// <param name="workingDirectory">工作区根目录</param>
    /// <param name="absolutePath">命中的绝对路径</param>
    /// <returns>相对工作区的路径，或绝对路径</returns>
    public static string ToPortablePath(string workingDirectory, string absolutePath)
    {
        string root = Path.GetFullPath(workingDirectory);
        string full = Path.GetFullPath(absolutePath);

        string relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative)
            ? full
            : relative;
    }
}
