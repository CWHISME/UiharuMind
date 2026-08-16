/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.Configs;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 构建 HarnessAgent 的配置：调用方交进来的那一份。
///
/// 这里只放<b>调用方知道而装配问不出来</b>的东西——角色、工作目录、权限档，
/// 以及几个每次请求现取的活钩子（会话模型/知识库/shell 放行来源、过程上报口）。
/// 装配还需要的其余事实（沙箱目录、工作区说明、技能源、MCP 工具集…）
/// 由 <see cref="AgentAssemblyPlan"/> 自己去解析，不劳调用方填。
/// </summary>
public class AgentBuildProfile
{
    /// <summary>
    /// 驱动整个装配的角色：<see cref="CharacterData.Kind"/> 决定是否装配工具与工作目录，
    /// Template 与对话模板决定系统提示。
    /// </summary>
    public required CharacterData Character { get; init; }

    /// <summary>绑定的工作目录;为空表示通用助手模式(文件/shell 工具落到沙箱目录)</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>权限档</summary>
    public EAgentPermissionMode PermissionMode { get; init; } = EAgentPermissionMode.AutoEdit;

    /// <summary>预授权 shell 命令模式(定时任务无人值守用)</summary>
    public IReadOnlyList<string>? PreAuthorizedShellPatterns { get; init; }

    /// <summary>额外的提示词模板参数(会话的 CustomParams)</summary>
    public IReadOnlyDictionary<string, object?>? PromptArguments { get; init; }

    /// <summary>
    /// 会话级模型来源。会话可绑定专属模型(如识图技能解析出的视觉模型),
    /// 惰性客户端每次请求时经此取值,优先于全局当前模型;为空则只用全局模型。
    /// </summary>
    public Func<ModelRunningData?>? SessionModelSource { get; init; }

    /// <summary>
    /// 会话级知识库来源(knowledge_search 工具执行时解析,锁定当前挂接会话的单库)
    /// </summary>
    public Func<MemoryData?>? SessionKnowledgeSource { get; init; }

    /// <summary>
    /// 会话级 shell 放行模式来源(审批规则每次执行时解析,
    /// 用户点"记住同类命令"后立即生效,无需重建装配)
    /// </summary>
    public Func<IReadOnlyList<string>?>? SessionShellApprovalSource { get; init; }

    /// <summary>
    /// 委派型工具的过程上报口(子代理与识图)。由执行者提供,指向它本轮的输出通道;
    /// 为空表示过程不外显。<b>只被渲染,不进历史也不回喂模型</b>——见 <see cref="ToolActivityContent"/>。
    /// </summary>
    public Action<AIContent>? ActivitySink { get; init; }
}
