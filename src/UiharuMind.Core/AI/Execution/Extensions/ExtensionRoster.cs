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
using UiharuMind.Core.AI.Execution.Prompts;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 系统提示里那份扩展名单（ADR 0079）：角色没禁的内置扩展，每个一句话简介。
/// 命令与参数不在这里、也不在 <c>ExCall</c> 描述里写死：一律经 <c>ExHelp</c> 现查。
///
/// <b>MCP server 的名字不进名单</b>（ADR 0096 补记）：那些名字跟着用户配置变，
/// 写进提示就等于每接一台、每改一次说明都换一次缓存前缀；而「配了哪些」本来就有
/// <c>mcp.list</c> 现查，比名单上多一行没头没尾的 <c>- godot-ai</c> 有用得多。
/// 名单因此只由「角色禁了哪些内置扩展」决定，与 MCP 配置无关。
/// </summary>
internal static class ExtensionRoster
{
    /// <summary>
    /// 名单
    /// </summary>
    /// <param name="config">生效能力</param>
    /// <param name="scope">场合（worktree 只进主代理/子代理的名单）</param>
    /// <returns>内置扩展名与一句话说明</returns>
    public static IReadOnlyList<(string Name, string Description)> Of(AgentToolConfig config, EAgentToolScope scope) =>
        AgentToolsetBuilder.ListedBuiltins(config, scope)
            .Select(name => (name, BuiltinExtensions.Summary(name))).ToList();

    /// <summary>
    /// 「# 扩展」整段
    /// </summary>
    /// <param name="config">生效能力</param>
    /// <param name="scope">场合</param>
    /// <returns>整段；名单为空时为空串</returns>
    public static string Section(AgentToolConfig config, EAgentToolScope scope)
    {
        IReadOnlyList<(string Name, string Description)> roster = Of(config, scope);
        return roster.Count == 0
            ? string.Empty
            : $"{AgentPromptHeadings.Extensions}\n\n{AgentToolPrompts.BuildExtensions(roster)}";
    }
}
