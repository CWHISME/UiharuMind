/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 子代理类型。不同类型配不同模型、不同权限边界、不同提示词侧重点。
/// </summary>
public enum ESubAgentType
{
    /// <summary>
    /// 通用子代理：权限继承主代理（FullAuto 下可改文件），用主代理的模型。
    /// 适合需要实际修改操作的任务。
    /// </summary>
    General,

    /// <summary>
    /// 探索子代理：始终只读，配轻量模型（可在设置页 agent 专用页签里选）。
    /// 适合初级调研任务（通览文件、搜代码、研究主题），避免用高级模型做低价值工作。
    /// </summary>
    Explorer,
}

/// <summary>
/// 子代理策略：把"类型差异"拢在一处——模型源、权限边界、工具描述、提示词侧重点。
/// </summary>
public sealed record SubAgentProfile
{
    /// <summary>子代理类型</summary>
    public required ESubAgentType Type { get; init; }

    /// <summary>
    /// 工具名。主代理看到这个名字就知道用途，不需要读参数说明。
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>工具描述（发给主代理的说明书）</summary>
    public required string Description { get; init; }

    /// <summary>
    /// 是否强制只读。探索型始终只读；通用型继承主代理的权限档。
    /// </summary>
    public bool ForceReadOnly => Type == ESubAgentType.Explorer;

    /// <summary>
    /// 提示词侧重点：探索型强调"调研后回报"，通用型强调"执行任务要求的改动"。
    /// </summary>
    public string RoleHint => Type == ESubAgentType.Explorer
        ? "你是探索子代理，专门做初级调研：通览文件、搜代码、研究主题。"
        : "你是通用子代理，可以执行实际修改操作。";

    /// <summary>
    /// 各类型子代理用各自配置的模型,未配置时回退到主代理模型。
    /// </summary>
    /// <param name="config">全局 agent 设置</param>
    /// <returns>模型名；空串表示用主代理模型</returns>
    public string ResolveModelName(AgentSettingConfig config)
    {
        return Type switch
        {
            ESubAgentType.Explorer => config.ExplorerSubAgentModelName,
            _ => config.GeneralSubAgentModelName,
        };
    }

    /// <summary>通用子代理策略</summary>
    public static SubAgentProfile General { get; } = new()
    {
        Type = ESubAgentType.General,
        ToolName = SubAgentTool.ToolGeneralName,
        // 两段描述共用一条<b>互斥判据</b>:这次委派要不要改变任何东西。
        // 从前两边各说各的(一边"要改东西时用我",一边"要读很多东西时用我"),而大量任务
        // 两头都沾,模型就倒向描述覆盖面更宽的探索档。判据里还要有"拿不准就用这个",
        // 否则中间地带仍然无主。
        Description =
            "Delegate a task to a general-purpose sub-agent and get back a focused report. " +
            "Use it whenever the task may need to CHANGE anything - editing files, running commands, " +
            "using MCP tools - or when you are not sure whether it will. This is the default choice; " +
            "only prefer " + SubAgentTool.ToolExplorerName + " when the task is purely about finding things out. " +
            "It has the same tools and permission mode as you, and anything needing approval " +
            "is asked of the user as usual. It runs to completion before this returns.",
    };

    /// <summary>探索子代理策略</summary>
    public static SubAgentProfile Explorer { get; } = new()
    {
        Type = ESubAgentType.Explorer,
        ToolName = SubAgentTool.ToolExplorerName,
        Description =
            "Delegate a READ-ONLY investigation to a lightweight sub-agent and get back a focused report. " +
            "Use it only when the task is purely about finding things out - surveying many files, " +
            "searching code, researching a topic on the web - so the raw material never enters your own context. " +
            "It CANNOT edit files, run commands or use MCP tools: if the task might need any of those, " +
            "use " + SubAgentTool.ToolGeneralName + " instead. " +
            "It may run on a cheaper model. It runs to completion before this returns.",
    };
}
