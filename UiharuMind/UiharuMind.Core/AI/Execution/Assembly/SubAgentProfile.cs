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
    /// 通用子代理：权限继承主 agent（FullAuto 下可改文件），用主 agent 的模型。
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
    /// 工具名。主 agent 看到这个名字就知道用途，不需要读参数说明。
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>工具描述（发给主 agent 的说明书）</summary>
    public required string Description { get; init; }

    /// <summary>
    /// 是否强制只读。探索型始终只读；通用型继承主 agent 的权限档。
    /// </summary>
    public bool ForceReadOnly => Type == ESubAgentType.Explorer;

    /// <summary>
    /// 提示词侧重点：探索型强调"调研后回报"，通用型强调"执行任务要求的改动"。
    /// </summary>
    public string RoleHint => Type == ESubAgentType.Explorer
        ? "你是探索子代理，专门做初级调研：通览文件、搜代码、研究主题。"
        : "你是通用子代理，可以执行实际修改操作。";

    /// <summary>
    /// 各类型子代理用各自配置的模型,未配置时回退到主 agent 模型。
    /// </summary>
    /// <param name="config">全局 agent 设置</param>
    /// <returns>模型名；空串表示用主 agent 模型</returns>
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
        Description =
            "Delegate a task to a general-purpose sub-agent and get back a focused report. " +
            "Use it for tasks that may need actual modifications (in FullAuto mode). " +
            "The sub-agent runs to completion before this returns. " +
            "It has the same permissions as you, minus anything that would need approval.",
    };

    /// <summary>探索子代理策略</summary>
    public static SubAgentProfile Explorer { get; } = new()
    {
        Type = ESubAgentType.Explorer,
        ToolName = SubAgentTool.ToolExplorerName,
        Description =
            "Delegate an exploration task to a lightweight read-only sub-agent. " +
            "Use it for broad exploration (surveying many files, researching a topic on the web) " +
            "so the raw material never enters your own context. " +
            "This sub-agent is always read-only and may use a cheaper model. " +
            "It runs to completion before this returns.",
    };
}
