/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Prompts;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 系统提示里那份扩展名单（ADR 0079）：角色没禁的内置扩展，加上可用的 MCP server，每个一句话简介。
/// 命令与参数不在这里、也不在 <c>ExCall</c> 描述里写死：一律经 <c>ExHelp</c> 现查
/// </summary>
internal static class ExtensionRoster
{
    /// <summary>
    /// 名单
    /// </summary>
    /// <param name="config">生效能力</param>
    /// <param name="mcp">同一次装配的 MCP 名单</param>
    /// <param name="scope">场合（worktree 只进主代理/子代理的名单）</param>
    /// <returns>名字与一句话说明，内置在前</returns>
    public static IReadOnlyList<(string Name, string Description)> Of(AgentToolConfig config, McpToolSet mcp,
        EAgentToolScope scope)
    {
        List<(string Name, string Description)> roster = AgentToolsetBuilder.ListedBuiltins(config, scope)
            .Select(name => (name, BuiltinExtensions.Summary(name))).ToList();
        // 与内置撞名的 server 被内置挡住，帮助里会说明；名单里不写，免得模型以为有两个
        roster.AddRange(mcp.Servers.Where(x => BuiltinExtensions.Canonical(x.Name) == null)
            .Select(x => (ExtensionCalls.ModelName(x.Name), x.Description)));
        return roster;
    }

    /// <summary>
    /// 「# 扩展」整段
    /// </summary>
    /// <param name="config">生效能力</param>
    /// <param name="mcp">同一次装配的 MCP 名单</param>
    /// <param name="scope">场合</param>
    /// <returns>整段；名单为空时为空串</returns>
    public static string Section(AgentToolConfig config, McpToolSet mcp, EAgentToolScope scope)
    {
        IReadOnlyList<(string Name, string Description)> roster = Of(config, mcp, scope);
        return roster.Count == 0
            ? string.Empty
            : $"{AgentPromptHeadings.Extensions}\n\n{AgentToolPrompts.BuildExtensions(roster)}";
    }
}
