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
/// 系统提示里那份扩展名单（ADR 0079 修订）：<b>只列 MCP server</b>。
/// 内置扩展的名字、命令、参数全在 <c>ExCall</c> 描述的速查里（常量），这里再列就是两份、且按角色过滤后还对不上；
/// MCP 是用户接的、模型无从知道，名单是它想到去查的唯一入口
/// </summary>
internal static class ExtensionRoster
{
    /// <summary>
    /// 名单
    /// </summary>
    /// <param name="mcp">同一次装配的 MCP 名单</param>
    /// <returns>名字与一句话说明</returns>
    public static IReadOnlyList<(string Name, string Description)> Of(McpToolSet mcp) =>
        // 与内置撞名的 server 被内置挡住，帮助里会说明；名单里不写，免得模型以为有两个
        mcp.Servers.Where(x => BuiltinExtensions.Canonical(x.Name) == null)
            .Select(x => (x.Name, x.Description)).ToList();

    /// <summary>
    /// 「# 扩展」整段
    /// </summary>
    /// <param name="mcp">同一次装配的 MCP 名单</param>
    /// <returns>整段；没有 MCP 时为空串</returns>
    public static string Section(McpToolSet mcp)
    {
        IReadOnlyList<(string Name, string Description)> roster = Of(mcp);
        return roster.Count == 0
            ? string.Empty
            : $"{AgentPromptHeadings.Extensions}\n\n{AgentToolPrompts.BuildExtensions(roster)}";
    }
}
