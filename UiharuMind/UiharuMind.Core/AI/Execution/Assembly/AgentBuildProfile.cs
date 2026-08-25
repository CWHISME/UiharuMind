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
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.Configs;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.ToolCall;

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
    /// 会话级知识库来源(KnowledgeSearch 工具执行时解析,锁定当前挂接会话的单库)
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

    /// <summary>
    /// 本次装配面对的模型：会话绑定的优先，回落全局当前模型。
    /// 与 <c>LazyChatClient</c> 同一解析次序——识图工具挂不挂由它定，
    /// <see cref="AgentAssemblyFacts"/> 与 <see cref="AgentAssemblyPlan"/> 都读这一份。
    /// </summary>
    /// <returns>当前模型；一个都没有则为 null</returns>
    public ModelRunningData? ResolveCurrentModel()
    {
        return SessionModelSource?.Invoke() ?? LlmManager.Instance.CurrentRunningModel;
    }

    /// <summary>
    /// 从会话构造。<b>「会话的哪些字段进装配」只有这一处定义</b>——
    /// 快照与装配都从产出的 profile 出发，因此这里漏一个字段，两边会一起漏，
    /// 而不会像从前那样一边读到、另一边读不到（子智能体名单就是这么漏的）。
    /// </summary>
    /// <param name="session">会话</param>
    /// <param name="sessionModelSource">会话级模型来源</param>
    /// <param name="sessionKnowledgeSource">会话级知识库来源</param>
    /// <param name="sessionShellApprovalSource">会话级 shell 放行模式来源</param>
    /// <param name="activitySink">委派型工具的过程上报口</param>
    /// <returns>构建配置</returns>
    /// <summary>
    /// 本会话的产出目录名（相对 <c>AgentOutputLayout.RootPath</c>）；无会话时为空串。
    ///
    /// 只带名字不带完整路径，是因为<b>建目录是副作用</b>，只允许发生在
    /// <c>AgentAssemblyPlan.Resolve</c> 里。名字随会话标题变，因此它也进装配快照
    /// ——提示词里逐字写着这个路径，改了标题不重建就等于告诉模型一个已经不用的目录。
    /// </summary>
    public string OutputFolderName { get; init; } = string.Empty;

    public static AgentBuildProfile FromSession(ChatSession session,
        Func<ModelRunningData?>? sessionModelSource = null,
        Func<MemoryData?>? sessionKnowledgeSource = null,
        Func<IReadOnlyList<string>?>? sessionShellApprovalSource = null,
        Action<AIContent>? activitySink = null)
    {
        return new AgentBuildProfile
        {
            Character = session.CharacterData,
            WorkspacePath = session.WorkspacePath,
            PermissionMode = (EAgentPermissionMode)Math.Clamp(session.PermissionModeIndex, 0, 2),
            PreAuthorizedShellPatterns = session.PreAuthorizedShellPatterns,
            PromptArguments = session.CustomParams,
            OutputFolderName = AgentOutputLayout.GetFolderName(session.Title, session.SessionId),
            SessionModelSource = sessionModelSource,
            SessionKnowledgeSource = sessionKnowledgeSource,
            SessionShellApprovalSource = sessionShellApprovalSource,
            ActivitySink = activitySink,
        };
    }

    /// <summary>
    /// 从<b>尚不存在的会话</b>构造：智能体页的会话是懒建的，首轮发送前没有 <see cref="ChatSession"/>，
    /// 但界面此时就要回答「这个会话会挂上什么、占多少」。
    ///
    /// 只有角色、工作区、权限档三项——它们正是<see cref="FromSession"/>里
    /// 会影响装配产物的那几项；其余（自定义模板参数、会话级模型/知识库、过程上报口）
    /// 要么此刻还不存在，要么只影响运行不影响挂了什么。
    /// </summary>
    /// <param name="character">将要使用的角色</param>
    /// <param name="workspacePath">将要绑定的工作目录</param>
    /// <param name="permissionModeIndex">权限档序号（越界自动收进合法范围，同 <see cref="FromSession"/>）</param>
    /// <returns>构建配置</returns>
    public static AgentBuildProfile FromDraft(CharacterData character, string? workspacePath,
        int permissionModeIndex)
    {
        return new AgentBuildProfile
        {
            Character = character,
            WorkspacePath = workspacePath,
            PermissionMode = (EAgentPermissionMode)Math.Clamp(permissionModeIndex, 0, 2),
        };
    }
}
