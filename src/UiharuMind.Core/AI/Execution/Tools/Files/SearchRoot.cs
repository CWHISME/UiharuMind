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
    /// 解析搜索范围：没传就是工作区根，绝对路径直接用，相对路径拼工作区根。
    /// 范围可以是目录（在其下递归搜）或单个文件（只搜它）。
    /// </summary>
    /// <param name="workingDirectory">工作区根目录</param>
    /// <param name="path">调用方给的搜索范围，可为 null/空</param>
    /// <returns>绝对路径</returns>
    public static string Resolve(string workingDirectory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Path.GetFullPath(workingDirectory);

        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(workingDirectory, path));
    }

    /// <summary>
    /// 搜索结果里一条路径该<b>怎么写给调用方</b>：能相对工作区就相对，否则给绝对路径。
    ///
    /// 基准必须是<b>工作区根</b>，不是本次的搜索根。从前两个搜索器都按搜索根算相对路径，
    /// 于是 <c>Grep(path: "Core")</c> 回来的 <c>AI/Foo.cs</c> 喂给 <c>Read</c> 会解析到
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

    /// <summary>
    /// 计算离 <paramref name="absolutePath"/> 最近的、存在于工作区内的祖先目录，
    /// 供搜索失败时回显「从哪一级起路径断掉」——<c>X 存在、X/Y 不存在</c> 比整条路径不算更能定位。
    ///
    /// 只回这一个值，<b>不列候选内容</b>（那会让模型挑顺眼的兄弟名接着干，滑进"降级猜测"）；
    /// 也不外探到<b>工作区边界之外</b>——落在工作区外的路径返回 null，工作区根目录不存在也返回 null。
    /// 渲染层对 null 回退到原有的文案。
    /// </summary>
    /// <param name="root">工作区根目录</param>
    /// <param name="absolutePath">解析之后的目标绝对路径（不存在）</param>
    /// <returns>最近的存在于工作区内的祖先目录；找不到为 null</returns>
    public static string? NearestExistingAncestorWithin(string root, string absolutePath)
    {
        string ws = Path.GetFullPath(root);
        string cur = Path.GetFullPath(absolutePath);

        while (true)
        {
            string rel = Path.GetRelativePath(ws, cur).Replace('\\', '/');
            bool within = !(rel.StartsWith("../", StringComparison.Ordinal)
                            || Path.IsPathFullyQualified(rel));
            if (!within) return null; // 退到了工作区之外，不再往外报

            if (Directory.Exists(cur)) return cur;

            // 文件系统根时 GetDirectoryName 返回 null：parent 为空即到顶，兜底返回 null
            string parent = Path.GetDirectoryName(cur) ?? string.Empty;
            if (string.IsNullOrEmpty(parent)) return null;
            cur = parent;
        }
    }
}
