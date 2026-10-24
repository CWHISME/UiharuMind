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
    /// 脑（ADR 0082）：同步、有界、只读的小模型整理助手。恒定只读，配脑模型
    /// （<c>AgentSettingConfig.BrainSubAgentModelName</c>），只挂 Grep/Read。
    /// 任务是整理/总结大文件与被截断的工具结果，可被追问。
    /// </summary>
    Brain,

    /// <summary>
    /// 探索档：异步后台、恒定只读的子代理，配探索模型（
    /// <c>AgentSettingConfig.ExplorerSubAgentModelName</c>），挂只读文件工具（含 Glob）与联网。
    /// 曾因 ADR 0044 归一委派而退役为墓碑，现复活为独立档：区别于脑（同步、有界、整理），
    /// 探索是开放、后台的调研/审阅。存量已确认无残留，枚举字符串值延续以便导入旧盘。
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
    // ADR 0044 起档位不再决定工具名（现为 CreateAgent 新开 / SendMessage 续聊，ADR 0065），
    // 说明书统一收在 SubAgentToolPrompts，两个字段随之退役。
    // 本 record 现在只剩「重建一次已存档的委派时要什么」：类型、只读与否、用哪个模型。

    /// <summary>
    /// 脑系谓词：只有脑。从前含墓碑档 Explorer（ADR 0044 封存、重建按脑），
    /// 复活为独立档后不再算脑系——右栏照常列探索条目，这里只剩真脑。
    /// 右栏分区（<c>SubAgentListViewData.Partition</c>）与脑追问（<c>BrainTool</c>）都从它来。
    /// </summary>
    public static bool IsBrainLineage(ESubAgentType type) => type == ESubAgentType.Brain;

    /// <summary>
    /// 是否强制只读。脑与探索档恒定只读；通用型继承主代理的权限档。
    /// <b>独立判据，不从 <see cref="IsBrainLineage"/> 推</b>：探索档只读但不算脑系。
    /// </summary>
    public bool ForceReadOnly => Type is ESubAgentType.Brain or ESubAgentType.Explorer;

    /// <summary>脑不挂 Glob：内容向，目录形状不关心（ADR 0082）。通用与探索档挂</summary>
    public bool MountsGlob => Type != ESubAgentType.Brain;

    /// <summary>内部工具轮上限：脑有界（<see cref="BrainTool.MaxIterations"/>），其余沿用委派上限</summary>
    public int MaxIterations => Type == ESubAgentType.Brain ? BrainTool.MaxIterations : SubAgentTool.MaxIterations;

    /// <summary>
    /// 装配侧是否按脑处理。只有真脑；探索档走自己的装配。
    /// <b>唯一判据在 <see cref="IsBrainLineage"/></b>，实例属性只是便捷入口——不会两边漂移。
    /// </summary>
    public bool IsBrain => IsBrainLineage(Type);

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
            ESubAgentType.Brain => config.BrainSubAgentModelName,
            ESubAgentType.Explorer => config.ExplorerSubAgentModelName,
            _ => config.GeneralSubAgentModelName,
        };
    }

    /// <summary>通用策略：新派出去的一律是这一档</summary>
    public static SubAgentProfile General { get; } = new() { Type = ESubAgentType.General };

    /// <summary>
    /// 脑策略（ADR 0082）：同步、有界、只读的整理助手，配脑模型。
    /// </summary>
    public static SubAgentProfile Brain { get; } = new() { Type = ESubAgentType.Brain };

    /// <summary>
    /// 探索策略：异步后台、恒定只读的调研/审阅助手，配探索模型。
    /// </summary>
    public static SubAgentProfile Explorer { get; } = new() { Type = ESubAgentType.Explorer };
}
