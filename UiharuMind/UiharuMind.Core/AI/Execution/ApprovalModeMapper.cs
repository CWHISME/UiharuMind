/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 会话权限档(产品概念),底层映射到框架的 AutoApprovalRules 组合
/// </summary>
public enum EAgentPermissionMode
{
    /// <summary>只读/计划:仅自动放行只读工具,其余逐一审批</summary>
    ReadOnly,

    /// <summary>自动编辑:<b>工作区内</b>的文件写入自动放行,shell 与越界写入仍需审批</summary>
    AutoEdit,

    /// <summary>完全自动:全部自动放行,只有<b>越界写入</b>要用户点一次</summary>
    FullAuto,
}

/// <summary>
/// 三档权限 → 框架 ToolApprovalAgentOptions.AutoApprovalRules 的映射器。
/// 另支持定时任务的 shell 命令预授权(glob 匹配)。
///
/// <b>档位语义只有这一处定义</b>——主 agent 与子代理都从这里取规则,否则"完全自动"在两边
/// 会渐渐变成两个意思(有不变量测试钉住)。
///
/// 一条贯穿三档的硬规则:<b>工作区外的写入永不自动放行</b>。用户点一次现成的"本会话允许"
/// 之后框架自己会把该工具整会话放行,所以这里不需要任何额外状态。它是有意做成"减法"而非
/// "加一条否决规则"的——框架的 AutoApprovalRules 是<b>或</b>语义,任一条返回 true 即放行,
/// 加否决规则拦不住 <c>AllToolsAutoApprovalRule</c>,只能把那条全放行规则本身换掉。
/// </summary>
public static class ApprovalModeMapper
{
    /// <summary>路径比较口径:除 Linux 外文件系统通常不区分大小写</summary>
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// 构建给定权限档的自动放行规则集
    /// </summary>
    /// <param name="mode">权限档</param>
    /// <param name="workspaceRoot">工作目录绝对路径,用于判定写入是否越界;空串表示无从判定(一律要审批)</param>
    /// <param name="preAuthorizedShellPatterns">预授权的 shell 命令 glob 模式(定时任务用),可空</param>
    /// <param name="sessionShellApprovalSource">会话级放行的 shell 命令模式来源
    /// (审批卡片"记住同类命令"写入,规则每次执行时现取现用,变化无需重建装配),可空</param>
    /// <returns>规则列表</returns>
    public static List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> BuildRules(
        EAgentPermissionMode mode, string workspaceRoot = "",
        IReadOnlyList<string>? preAuthorizedShellPatterns = null,
        Func<IReadOnlyList<string>?>? sessionShellApprovalSource = null)
    {
        string root = string.IsNullOrWhiteSpace(workspaceRoot) ? string.Empty : Path.GetFullPath(workspaceRoot);

        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules = new()
        {
            // 任何档位:技能只读工具自动放行。
            // 文件读取工具(Read/Glob/Grep)压根没包审批,不需要规则;写工具(Write/Edit)由
            // PermissiveFileAccessTools 包了 ApprovalRequiredAIFunction,放行与否全看下面这几条
            AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule,
        };

        switch (mode)
        {
            case EAgentPermissionMode.FullAuto:
                // 全放行,但越界写入除外——定时任务是代码写死的档位,用户没机会为它选,
                // 而无人值守下越界写入没人拦就真的没人拦了
                rules.Add(context =>
                    new ValueTask<bool>(!IsOutOfWorkspaceWrite(context.FunctionCallContent, root)));
                break;

            case EAgentPermissionMode.AutoEdit:
                // 这一档曾经<b>一条规则都不加</b>,于是与只读档行为完全一致:枚举注释写着
                // "文件读写自动放行",实际每次编辑都弹卡。更要命的是定时任务写死用这一档而
                // 无头执行一律拒绝审批,净效果是定时任务所有文件写入都被拒
                rules.Add(context => new ValueTask<bool>(
                    IsMutatingFileTool(context.FunctionCallContent) &&
                    !IsOutOfWorkspaceWrite(context.FunctionCallContent, root)));
                break;
        }

        if (preAuthorizedShellPatterns is { Count: > 0 })
        {
            List<string> patterns = new(preAuthorizedShellPatterns);
            rules.Add(context => new ValueTask<bool>(MatchesShellPattern(context.FunctionCallContent, patterns)));
        }

        if (sessionShellApprovalSource != null)
        {
            rules.Add(context =>
                new ValueTask<bool>(MatchesShellPattern(context.FunctionCallContent, sessionShellApprovalSource())));
        }

        return rules;
    }

    /// <summary>
    /// 从一条 shell 命令派生"同类命令"的放行模式:取前两个词加通配
    /// (如 "git status --short" → "git status*";单词命令 "ls" → "ls*")。
    /// 只取头部是刻意的——"git*" 会连 push 一起放行,过宽;前两词恰好圈住"同一子命令"。
    /// </summary>
    /// <param name="command">命令原文</param>
    /// <returns>glob 模式;空命令返回空串</returns>
    public static string DeriveCommandPattern(string command)
    {
        string[] tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return string.Empty;
        return tokens.Length == 1 ? $"{tokens[0]}*" : $"{tokens[0]} {tokens[1]}*";
    }

    private static bool MatchesShellPattern(FunctionCallContent functionCall, IReadOnlyList<string>? patterns)
    {
        if (patterns is not { Count: > 0 }) return false;
        if (functionCall.Name != CharacterRunnerFactory.ShellToolName) return false;
        string? command = ExtractCommand(functionCall.Arguments);
        if (string.IsNullOrEmpty(command)) return false;
        return patterns.Any(pattern => GlobMatch(pattern, command));
    }

    /// <summary>
    /// 从 shell 工具调用参数中取命令原文(审批卡片派生放行模式也用它)
    /// </summary>
    /// <param name="arguments">工具调用参数</param>
    /// <returns>命令文本;取不到为 null</returns>
    public static string? ExtractCommand(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("command", out object? value)) return null;
        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString(),
        };
    }

    /// <summary>
    /// 从文件工具调用参数中取目标路径(审批越界判定用)
    /// </summary>
    /// <param name="arguments">工具调用参数</param>
    /// <returns>路径文本;取不到为 null</returns>
    public static string? ExtractFilePath(IDictionary<string, object?>? arguments)
    {
        if (arguments == null) return null;
        if (!arguments.TryGetValue("filePath", out object? value) &&
            !arguments.TryGetValue("file_path", out value)) return null;

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString(),
        };
    }

    private static bool IsMutatingFileTool(FunctionCallContent functionCall)
        => FileToolNames.Mutating.Contains(functionCall.Name);

    /// <summary>
    /// 这次调用是否是「工作区外的写入」。
    ///
    /// 判据故意保守:是写工具、但<b>取不到路径或无从判定</b>时也算越界——宁可多问一次,
    /// 也不能让一个畸形参数悄悄越界落盘。
    ///
    /// 只比较规范化后的路径,<b>不解析符号链接</b>:工作区里一个指向外部的软链能绕过这条判据。
    /// 接受这个缺口,因为能这么干的模型同样能直接用 shell,而真正的边界是"用户点了那一下"——
    /// 每次审批都去 realpath 一趟只是把成本花在挡不住的地方。
    /// </summary>
    private static bool IsOutOfWorkspaceWrite(FunctionCallContent functionCall, string workspaceRoot)
    {
        if (!IsMutatingFileTool(functionCall)) return false;
        if (workspaceRoot.Length == 0) return true;

        string? path = ExtractFilePath(functionCall.Arguments);
        if (string.IsNullOrWhiteSpace(path)) return true;

        string full;
        try
        {
            full = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(workspaceRoot, path));
        }
        catch (Exception)
        {
            return true; //路径非法(含非法字符/过长)一样交给用户看一眼
        }

        string root = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar);
        return !full.Equals(root, PathComparison) &&
               !full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool GlobMatch(string pattern, string input)
    {
        if (pattern == "*") return true;
        string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(input, regexPattern, RegexOptions.Singleline);
    }
}
