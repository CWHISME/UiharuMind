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

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// 会话权限档(产品概念),底层映射到框架的 AutoApprovalRules 组合
/// </summary>
public enum EAgentPermissionMode
{
    /// <summary>只读/计划:仅自动放行只读工具,其余逐一审批</summary>
    ReadOnly,

    /// <summary>自动编辑:文件读写自动放行,shell 等仍需审批</summary>
    AutoEdit,

    /// <summary>完全自动:全部自动放行</summary>
    FullAuto,
}

/// <summary>
/// 三档权限 → 框架 ToolApprovalAgentOptions.AutoApprovalRules 的映射器。
/// 另支持定时任务的 shell 命令预授权(glob 匹配)。
/// </summary>
public static class ApprovalModeMapper
{
    /// <summary>
    /// 构建给定权限档的自动放行规则集
    /// </summary>
    /// <param name="mode">权限档</param>
    /// <param name="preAuthorizedShellPatterns">预授权的 shell 命令 glob 模式(定时任务用),可空</param>
    /// <param name="sessionShellApprovalSource">会话级放行的 shell 命令模式来源
    /// (审批卡片"记住同类命令"写入,规则每次执行时现取现用,变化无需重建装配),可空</param>
    /// <returns>规则列表</returns>
    public static List<Func<FunctionCallContent, ValueTask<bool>>> BuildRules(
        EAgentPermissionMode mode, IReadOnlyList<string>? preAuthorizedShellPatterns = null,
        Func<IReadOnlyList<string>?>? sessionShellApprovalSource = null)
    {
        List<Func<FunctionCallContent, ValueTask<bool>>> rules = new()
        {
            // 任何档位:技能只读工具自动放行
            // 注意:file_access_* 工具已由 PermissiveFileAccessTools 自行包 ApprovalRequiredAIFunction,
            // 始终要求用户审批,故不再加入 FileAccessProvider.* 的按名自动放行规则(否则会被 AutoEdit 档自动放行)。
            AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule,
        };

        switch (mode)
        {
            case EAgentPermissionMode.FullAuto:
                rules.Add(ToolApprovalAgent.AllToolsAutoApprovalRule);
                break;
        }

        if (preAuthorizedShellPatterns is { Count: > 0 })
        {
            List<string> patterns = new(preAuthorizedShellPatterns);
            rules.Add(functionCall => new ValueTask<bool>(MatchesShellPattern(functionCall, patterns)));
        }

        if (sessionShellApprovalSource != null)
        {
            rules.Add(functionCall =>
                new ValueTask<bool>(MatchesShellPattern(functionCall, sessionShellApprovalSource())));
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
        if (functionCall.Name != AgentHost.ShellToolName) return false;
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

    private static bool GlobMatch(string pattern, string input)
    {
        if (pattern == "*") return true;
        string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(input, regexPattern, RegexOptions.Singleline);
    }
}
