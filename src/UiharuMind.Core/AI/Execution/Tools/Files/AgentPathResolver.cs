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
/// 工具路径参数的解析口径：绝对路径直接用，相对路径拼工作区根。
///
/// 文件工具、搜索、识图、审批越界判定、群产物区、界面的审批预演都经这一处。
/// 从前这条规则在八处各写一遍，改一处漏一处的后果是：执行落在 A、审批判的是 B、卡片预演的是 C。
///
/// 值语义（record）：缓存按口径比对时两份相同的配置视为同一份。
/// </summary>
public sealed record AgentPathResolver
{
    /// <summary>
    /// 构造解析口径
    /// </summary>
    /// <param name="workspaceRoot">工作区根；空表示没有工作区，此时相对路径无从解析</param>
    public AgentPathResolver(string? workspaceRoot)
    {
        WorkspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot) ? string.Empty : Path.GetFullPath(workspaceRoot);
    }

    /// <summary>工作区根（已规范化）；空串表示没有工作区</summary>
    public string WorkspaceRoot { get; }

    /// <summary>
    /// 解析路径参数
    /// </summary>
    /// <param name="path">模型给的路径</param>
    /// <param name="relativeTo">相对路径的基准；省略即工作区根（Glob 的模式退化成路径时相对的是搜索根）</param>
    /// <returns>规范化的绝对路径</returns>
    /// <exception cref="ArgumentException">路径不成形（非法字符等），或相对路径没有基准可拼</exception>
    public string Resolve(string path, string? relativeTo = null)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

        string baseDirectory = relativeTo ?? WorkspaceRoot;
        if (baseDirectory.Length == 0)
        {
            throw new ArgumentException($"Relative path '{path}' has no working directory to resolve against.");
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    /// <summary>
    /// 同 <see cref="Resolve"/>，但解析不了时返回 false 而不抛（审批、预演这类只读判断用）
    /// </summary>
    /// <param name="path">模型给的路径；空视为解析不了</param>
    /// <param name="fullPath">规范化的绝对路径；失败时为空串</param>
    /// <returns>是否解析成功</returns>
    public bool TryResolve(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            fullPath = Resolve(path);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// 一条路径该<b>怎么写回给模型</b>：能相对工作区就相对，否则给绝对路径。
    ///
    /// 基准必须是<b>工作区根</b>，不是本次的搜索根。从前两个搜索器都按搜索根算相对路径，
    /// 于是 <c>Grep(path: "Core")</c> 回来的 <c>AI/Foo.cs</c> 喂给 <c>Read</c> 会解析到
    /// <c>&lt;工作区&gt;/AI/Foo.cs</c> —— 找不到。模型吃过几次之后就只信绝对路径了，
    /// 而那是它<b>理性</b>的选择：绝对路径是当时唯一跨工具通用的形式。
    /// 改成按工作区根算，搜索结果才第一次可以直接当 <c>Read</c> 的入参。
    ///
    /// 落在工作区之外时不硬凑相对路径（那会得到一串 <c>../../</c>），直接给绝对路径。
    /// </summary>
    /// <param name="absolutePath">绝对路径</param>
    /// <returns>相对工作区的路径，或绝对路径</returns>
    public string ToPortable(string absolutePath)
    {
        string full = Path.GetFullPath(absolutePath);
        if (WorkspaceRoot.Length == 0) return full;

        string relative = Path.GetRelativePath(WorkspaceRoot, full).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative)
            ? full
            : relative;
    }
}
