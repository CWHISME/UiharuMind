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
/// 工具路径参数的解析口径：绝对路径直接用，相对路径拼工作区根，
/// 以草稿目录简写（<c>$DRAFT</c> 及其各 shell 写法）开头的展开到草稿目录。
///
/// 文件工具、搜索、识图、审批越界判定、群产物区、界面的审批预演都经这一处。
/// 从前这条规则在八处各写一遍，改一处漏一处的后果是：执行落在 A、审批判的是 B、卡片预演的是 C。
///
/// 简写是为了让模型不必手抄草稿目录：那条路径带两段哈希，模型嫌长就绕开它，
/// 把临时文件散进项目里；抄错一位又会被判成跨会话写入。shell 里它是同名环境变量，
/// 这里展开同一个名字，两边写法一致。
///
/// 值语义（record）：缓存按口径比对时两份相同的配置视为同一份。
/// </summary>
public sealed record AgentPathResolver
{
    /// <summary>草稿目录简写的环境变量名（shell 里导出的就是它）</summary>
    public const string DraftVariable = "DRAFT";

    /// <summary>简写在各 shell 里的写法。文件工具四种都认：模型照着哪种 shell 学的就会写哪种</summary>
    private static readonly string[] DraftSpellings =
        ["$" + DraftVariable, "${" + DraftVariable + "}", "$env:" + DraftVariable, "%" + DraftVariable + "%"];

    /// <summary>
    /// 构造解析口径
    /// </summary>
    /// <param name="workspaceRoot">工作区根；空表示没有工作区，此时相对路径无从解析</param>
    /// <param name="draftRoot">草稿目录；空表示没有，此时简写报错而不是当成相对路径</param>
    /// <param name="shellBinary">会话的 shell，决定写回路径时用哪种简写（见 <see cref="DraftTokenFor"/>）</param>
    public AgentPathResolver(string? workspaceRoot, string? draftRoot = null, string? shellBinary = null)
    {
        WorkspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot) ? string.Empty : Path.GetFullPath(workspaceRoot);
        DraftRoot = string.IsNullOrWhiteSpace(draftRoot) ? string.Empty : Path.GetFullPath(draftRoot);
        DraftToken = DraftTokenFor(shellBinary);
    }

    /// <summary>工作区根（已规范化）；空串表示没有工作区</summary>
    public string WorkspaceRoot { get; }

    /// <summary>草稿目录（已规范化）；空串表示没有</summary>
    public string DraftRoot { get; }

    /// <summary>写回路径时用的简写（与提示词里教的是同一种）</summary>
    public string DraftToken { get; }

    /// <summary>
    /// 某个 shell 里草稿目录简写的写法。PowerShell 必须写 <c>$env:DRAFT</c>：
    /// 在那里 <c>$DRAFT</c> 是个未定义的 PS 变量，会静默展开成空串，<c>$DRAFT/a.py</c> 就成了 <c>/a.py</c>。
    /// 按文件名分类，与框架 <c>ShellResolver.ClassifyKind</c> 同一口径。
    /// </summary>
    /// <param name="shellBinary">shell 可执行路径；空即没有 shell</param>
    /// <returns>简写</returns>
    public static string DraftTokenFor(string? shellBinary)
    {
        string name = Path.GetFileNameWithoutExtension(shellBinary ?? string.Empty).ToUpperInvariant();
        return name switch
        {
            "PWSH" or "POWERSHELL" => "$env:" + DraftVariable,
            "CMD" => "%" + DraftVariable + "%",
            _ => "$" + DraftVariable,
        };
    }

    /// <summary>
    /// 解析路径参数
    /// </summary>
    /// <param name="path">模型给的路径</param>
    /// <param name="relativeTo">相对路径的基准；省略即工作区根（Glob 的模式退化成路径时相对的是搜索根）</param>
    /// <returns>规范化的绝对路径</returns>
    /// <exception cref="ArgumentException">路径不成形（非法字符等），相对路径没有基准可拼，或用了简写却没有草稿目录</exception>
    public string Resolve(string path, string? relativeTo = null)
    {
        if (TrySplitDraft(path, out string rest))
        {
            if (DraftRoot.Length == 0)
            {
                throw new ArgumentException($"'{path}' refers to the draft directory, but this session has none.");
            }

            return Path.GetFullPath(DraftRoot + rest);
        }

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
    /// 一条路径该<b>怎么写回给模型</b>：草稿目录里的写成简写，能相对工作区就相对，否则给绝对路径。
    /// 草稿目录里的也要写简写：模型照抄工具结果，回显长路径它下一次就又抄长路径。
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
    /// <returns>简写开头的路径、相对工作区的路径，或绝对路径</returns>
    public string ToPortable(string absolutePath)
    {
        string full = Path.GetFullPath(absolutePath);
        if (DraftRoot.Length > 0 && RelativeWithin(DraftRoot, full) is { } inDraft)
        {
            return inDraft == "." ? DraftToken : $"{DraftToken}/{inDraft}";
        }

        return WorkspaceRoot.Length > 0 && RelativeWithin(WorkspaceRoot, full) is { } inWorkspace ? inWorkspace : full;
    }

    /// 以简写开头（后面紧跟分隔符或就此结束）时切出余下部分；<c>$DRAFTS</c> 这类只是前缀撞上的不算。
    /// 分隔符按平台认：Unix 上反斜杠是文件名里的普通字符，当成分隔符会拼出名叫 "room\a.py" 的文件
    private static bool TrySplitDraft(string path, out string rest)
    {
        foreach (string spelling in DraftSpellings)
        {
            if (!path.StartsWith(spelling, StringComparison.Ordinal)) continue;

            rest = path[spelling.Length..];
            if (rest.Length == 0 || rest[0] == Path.DirectorySeparatorChar || rest[0] == Path.AltDirectorySeparatorChar)
            {
                return true;
            }
        }

        rest = string.Empty;
        return false;
    }

    /// 落在 root 之内时给相对路径（正斜杠，恰是 root 本身为 "."），之外为 null
    private static string? RelativeWithin(string root, string full)
    {
        string relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) || relative == ".." || Path.IsPathFullyQualified(relative)
            ? null
            : relative;
    }
}
