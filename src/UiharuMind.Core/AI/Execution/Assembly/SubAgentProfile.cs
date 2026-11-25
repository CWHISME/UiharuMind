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
    /// （<c>AgentSettingConfig.BrainSubAgentModelNames</c>，可配多个、派活依次取用），只挂 Grep/Read。
    /// 任务是整理/总结大文件与被截断的工具结果，可被追问。
    /// </summary>
    Brain,

    /// <summary>
    /// 探索档：异步后台子代理，配探索模型（
    /// <c>AgentSettingConfig.ExplorerSubAgentModelNames</c>，同样可配多个）。工具集与通用档相同
    /// （含 Glob 与联网），边界由权限档回答。
    /// 曾因 ADR 0044 归一委派而退役为墓碑，现复活为独立档：区别于脑（同步、有界、整理），
    /// 探索是开放、后台的调研。存量已确认无残留，枚举字符串值延续以便导入旧盘。
    /// </summary>
    Explorer,

    /// <summary>
    /// 评审档（ADR 0112）：异步后台的评审者，配评审模型
    /// （<c>AgentSettingConfig.ReviewerSubAgentModelNames</c>）。
    ///
    /// 与通用档、探索档的<b>工具集完全相同</b>，能不能改东西由权限档回答。差别只在产出形状：
    /// 探索是「翻材料找结论」，评审是「对着已有产物（改动、文档、方案）给意见」——
    /// 一句总体判断 + 按严重度排的问题清单。所以它的区别落在身份卡、提示词纪律与模型上，
    /// 不在权限边界上。
    /// </summary>
    Reviewer,
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
    /// 是否强制只读。只有脑：它同步、有界、只拿 Grep/Read（ADR 0082）。
    ///
    /// <b>委派的三档（通用 / 探索 / 评审）都不在这里</b>：它们的工具集完全相同，
    /// 能不能改东西一概由<b>权限档</b>回答——档位只决定产出纪律、身份卡与模型（ADR 0112 修订）。
    /// 曾经探索档恒定只读（ADR 0021 / 0044 / 0075 的产品决定），现在这条没了：
    /// 派活者把权限档调成「自动编辑 / 完全自动」，派出去做调研或评审的子代理也就能改文件。
    /// </summary>
    public bool ForceReadOnly => Type == ESubAgentType.Brain;

    /// <summary>脑不挂 Glob：内容向，目录形状不关心（ADR 0082）。其余档挂（评审要看目录形状）</summary>
    public bool MountsGlob => Type != ESubAgentType.Brain;

    /// <summary>内部工具轮上限：脑有界（<see cref="BrainTool.MaxIterations"/>），其余沿用委派上限</summary>
    public int MaxIterations => Type == ESubAgentType.Brain ? BrainTool.MaxIterations : SubAgentTool.MaxIterations;

    /// <summary>
    /// 装配侧是否按脑处理。只有真脑；委派的三档都走同一份委派装配。
    /// <b>唯一判据在 <see cref="IsBrainLineage"/></b>，实例属性只是便捷入口——不会两边漂移。
    /// </summary>
    public bool IsBrain => IsBrainLineage(Type);

    // 从前这里还有一个 RoleHint（「你只读：通览文件、搜代码…」/「你可以执行实际修改操作…」），
    // 与 SubAgentPrompts.BoundaryReadOnly / BoundaryCanMutate 近乎逐字同义，
    // 一个挂在「# 角色」、一个挂在「# 做法」，同一份提示词里把同一件事说了两遍。
    // 已删：边界那一句由装配侧按 canMutate 选（现在是「有没有挂写工具」，不再看档位）。

    /// <summary>
    /// 各类型子代理用各自配置的模型,未配置时回退到主代理模型。
    ///
    /// <b>返回的是一份候选名单而不只是一个名字</b>:同一档可以配多个模型,派活依次取用
    /// （<see cref="SubAgentModelRotation"/>，ADR 0111）——主代理一轮里连着派出几个子代理时，
    /// 全落在同一个模型上会撞供应商的并发限额，表现就是「一委派就卡」。
    /// </summary>
    /// <param name="config">全局 agent 设置</param>
    /// <returns>候选模型名的<b>快照</b>，按用户配置顺序；空列表表示用主代理模型</returns>
    public IReadOnlyList<string> ResolveModelNames(AgentSettingConfig config)
    {
        List<string> names = Type switch
        {
            ESubAgentType.Brain => config.BrainSubAgentModelNames,
            ESubAgentType.Explorer => config.ExplorerSubAgentModelNames,
            ESubAgentType.Reviewer => config.ReviewerSubAgentModelNames,
            _ => config.GeneralSubAgentModelNames,
        };

        // 拷贝一份再交出去：写方（设置页、装配重建）都是换实例，所以拷到的一定是
        // 一个发布后就不再变的列表。这条安全靠的是那个约定——ToArray 本身不是原子的，
        // 真有人在原地增删的话这里仍可能抛或读到中间态。拷贝只是多一层兜底
        return names.ToArray();
    }

    /// <summary>通用策略：新派出去的一律是这一档</summary>
    public static SubAgentProfile General { get; } = new() { Type = ESubAgentType.General };

    /// <summary>
    /// 脑策略（ADR 0082）：同步、有界、只读的整理助手，配脑模型。
    /// </summary>
    public static SubAgentProfile Brain { get; } = new() { Type = ESubAgentType.Brain };

    /// <summary>
    /// 探索策略：异步后台的调研助手，配探索模型。
    /// </summary>
    public static SubAgentProfile Explorer { get; } = new() { Type = ESubAgentType.Explorer };

    /// <summary>
    /// 评审策略（ADR 0112）：异步后台的评审者，配评审模型。
    /// </summary>
    public static SubAgentProfile Reviewer { get; } = new() { Type = ESubAgentType.Reviewer };

    /// <summary>
    /// 按类型取档案。<b>类型 → 档案的唯一映射处</b>：装配、轮次上限、模型源都从这里取，
    /// 各写一份 switch 迟早漏一个分支（那种漏法不会报错，只会悄悄落进 default）。
    ///
    /// 另外两处按类型分流的地方不在这里，别把它们算进来：身份卡走
    /// <see cref="SubAgentDefaultCharacter.IdentityIdOf(ESubAgentType)"/>（它有自己的 switch，
    /// 有测试逐个枚举值钉住），右栏「只读」标记走会话自己的权限档而不是档位（ADR 0112 修订）。
    /// </summary>
    /// <param name="type">子代理类型</param>
    /// <returns>该类型的档案</returns>
    public static SubAgentProfile Of(ESubAgentType type) => type switch
    {
        ESubAgentType.Brain => Brain,
        ESubAgentType.Explorer => Explorer,
        ESubAgentType.Reviewer => Reviewer,
        _ => General,
    };
}
