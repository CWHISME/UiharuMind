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
    /// <returns>规则列表</returns>
    public static List<Func<FunctionCallContent, ValueTask<bool>>> BuildRules(
        EAgentPermissionMode mode, IReadOnlyList<string>? preAuthorizedShellPatterns = null)
    {
        List<Func<FunctionCallContent, ValueTask<bool>>> rules = new()
        {
            // 任何档位:只读文件工具与技能只读工具自动放行
            FileAccessProvider.ReadOnlyToolsAutoApprovalRule,
            AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule,
        };

        switch (mode)
        {
            case EAgentPermissionMode.AutoEdit:
                rules.Add(FileAccessProvider.AllToolsAutoApprovalRule);
                break;
            case EAgentPermissionMode.FullAuto:
                rules.Add(ToolApprovalAgent.AllToolsAutoApprovalRule);
                break;
        }

        if (preAuthorizedShellPatterns is { Count: > 0 })
        {
            List<string> patterns = new(preAuthorizedShellPatterns);
            rules.Add(functionCall => new ValueTask<bool>(MatchesShellPattern(functionCall, patterns)));
        }

        return rules;
    }

    private static bool MatchesShellPattern(FunctionCallContent functionCall, List<string> patterns)
    {
        if (functionCall.Name != AgentHost.ShellToolName) return false;
        string? command = ExtractCommand(functionCall.Arguments);
        if (string.IsNullOrEmpty(command)) return false;
        return patterns.Any(pattern => GlobMatch(pattern, command));
    }

    private static string? ExtractCommand(IDictionary<string, object?>? arguments)
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
