/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Prompts;
using UiharuMind.Core.AI.Execution;
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

    // 从前这里还有 ToolName 与 Description 两个字段：两档各挂一把工具、各带一份说明书。
    // ADR 0044 把三把工具归一为 SubAgentTool.ToolName 之后，档位不再决定工具名，
    // 说明书也只剩一份（SubAgentToolPrompts.SendMessageDescription），两个字段随之退役。
    // 本 record 现在只剩「重建一次已存档的委派时要什么」：类型、只读与否、用哪个模型。

    /// <summary>
    /// 是否强制只读。探索型始终只读；通用型继承主代理的权限档。
    /// </summary>
    public bool ForceReadOnly => Type == ESubAgentType.Explorer;

    // 从前这里还有一个 RoleHint（「你只读：通览文件、搜代码…」/「你可以执行实际修改操作…」），
    // 与 SubAgentPrompts.BoundaryReadOnly / BoundaryCanMutate 近乎逐字同义，
    // 一个挂在「# 角色」、一个挂在「# 做法」，同一份提示词里把同一件事说了两遍。
    // 已删：档位差异体现在 ForceReadOnly 上，边界那一句由装配侧按 canMutate 选。

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

    /// <summary>通用策略：新派出去的一律是这一档</summary>
    public static SubAgentProfile General { get; } = new() { Type = ESubAgentType.General };

    /// <summary>
    /// 只读策略。<b>不再有任何新委派落到这一档</b>（ADR 0044 退役了 RunReadOnlyAgent），
    /// 保留它<b>只为重建存量</b>：老的探索子会话身上还钉着 <c>ESubAgentType.Explorer</c>，
    /// 重建时必须照旧只读——拔掉这一档，那些已存档的只读子会话恢复后会变成可写。
    /// 彻底清除是 ADR 0044 的阶段 2。
    /// </summary>
    public static SubAgentProfile Explorer { get; } = new() { Type = ESubAgentType.Explorer };
}
