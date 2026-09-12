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

    /// <summary>
    /// 这次装配服务的会话标识；无会话（能力预览）时为空串。
    /// 子代理派活时要据此把子会话认回派活者（<c>ParentSessionId</c>）。
    /// </summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>绑定的工作目录;为空表示通用助手模式(文件/shell 工具落到沙箱目录)</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>
    /// 子会话身份。非 null 表示这次装配的是一个<b>子代理</b>，
    /// 装配走 <c>SubAgentAssembly</c> 而不是主 agent 那条路。
    ///
    /// 必须由会话字段推出、不能由调用方临时决定：重开一个子会话续跑时没人再传参数，
    /// 而按主 agent 那条路重建出来的 agent 能力更大——正是不变量禁止的那件事。
    /// </summary>
    public SubAgentIdentity? SubAgent { get; init; }

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
    /// 一次委派开始时把子会话标识交给界面（参数为工具调用标识与子会话标识）。
    /// 为空表示没有界面在看——见 <see cref="ToolCall.SubSessionStartedContent"/>。
    /// </summary>
    public Action<string, string>? SubSessionStarted { get; init; }

    /// <summary>
    /// 本轮有没有人看着（界面在渲染）。无人值守（定时任务）为 false。
    /// 子代理的墙钟上限据此分档——有人看着就按得动停止，没人看着才需要兜底。
    ///
    /// 是个 <c>Func</c> 而不是 <c>bool</c>：工具在装配时创建一次、跨轮次复用，
    /// 而"这一轮有没有人看着"每轮由 <c>TurnDriver</c> 交进来。
    /// </summary>
    public Func<bool>? IsAttendedSource { get; init; }

    /// <summary>
    /// 本轮的审批回应取得方式。由执行者提供，指向<b>派活者这一轮</b>的审批通道——
    /// 子代理跑自己的轮次时要用同一条，否则它产出的审批请求没人回应。
    /// 无人值守（定时任务）时指向「一律拒绝」那一份。为空表示不进入审批轮次。
    /// </summary>
    public Func<ApprovalResolver?>? SubAgentApprovalSource { get; init; }

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
    /// <param name="isAttendedSource">本轮有没有人看着</param>
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
        Func<bool>? isAttendedSource = null,
        Func<ApprovalResolver?>? subAgentApprovalSource = null,
        Action<string, string>? subSessionStarted = null)
    {
        return new AgentBuildProfile
        {
            Character = session.CharacterData,
            SessionId = session.SessionId,
            WorkspacePath = session.WorkspacePath,
            SubAgent = session.IsSubSession
                ? new SubAgentIdentity(session.ParentSessionId!, session.SubAgentType, session.SubAgentName)
                : null,
            PermissionMode = (EAgentPermissionMode)Math.Clamp(session.PermissionModeIndex, 0, 2),
            PreAuthorizedShellPatterns = session.PreAuthorizedShellPatterns,
            PromptArguments = session.CustomParams,
            OutputFolderName = AgentOutputLayout.GetFolderName(session.Title, session.SessionId),
            SessionModelSource = sessionModelSource,
            SessionKnowledgeSource = sessionKnowledgeSource,
            SessionShellApprovalSource = sessionShellApprovalSource,
            IsAttendedSource = isAttendedSource,
            SubAgentApprovalSource = subAgentApprovalSource,
            SubSessionStarted = subSessionStarted,
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

/// <summary>
/// 子会话的身份：它由谁派活、装配成哪一种子代理、点名了哪个子智能体。
/// 三项全部来自会话本体的持久化字段，重开即可原样重建。
/// </summary>
/// <param name="ParentSessionId">派活给它的那个会话</param>
/// <param name="Type">子代理档（通用 / 探索）</param>
/// <param name="AgentName">被点名的子智能体名；空串为通用匿名子代理</param>
public sealed record SubAgentIdentity(string ParentSessionId, ESubAgentType Type, string AgentName);
