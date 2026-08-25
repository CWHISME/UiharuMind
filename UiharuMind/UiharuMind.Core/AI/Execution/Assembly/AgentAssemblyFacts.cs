/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Python;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 一次 agent 装配所消费的输入里，<b>能廉价取得又能按值比较</b>的那一半（record 值相等比较）。
///
/// 两个用途是同一件事：它既是<c>装配</c>的前半段输入，又是<c>要不要重建装配</c>的判据。
/// 每次挂接重算一遍，与上次不等才往下解析贵的那一半（建目录、技能源、MCP 工具集，
/// 见 <see cref="AgentAssemblyPlan"/>）并真正重建。
/// 因此角色卡编辑、会话参数、能力开关、MCP 工具集变化都被直接捕获，
/// 而不必按字符串指纹"猜"重建时机。
///
/// 字段列表同时就是装配依赖的显式清单。不在其列的：
/// 模型（惰性客户端按请求解析，切换无需重建）、记忆库（按请求经闭包解析）、
/// 技能文件内容（框架运行期从磁盘读取，天然是活的）。
/// </summary>
public sealed record AgentAssemblyFacts
{
    /// <summary>角色标识</summary>
    public required string CharacterId { get; init; }

    /// <summary>角色种类(决定装配形态)</summary>
    public required ECharacterKind Kind { get; init; }

    /// <summary>重算好的系统提示词(角色模板+会话参数),角色卡与参数的变化经此显形</summary>
    public required string Instructions { get; init; }

    /// <summary>推理执行参数(温度等)的序列化形态</summary>
    public required string ExecutionSettings { get; init; }

    /// <summary>绑定的工作目录;角色扮演档恒为 null</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>权限档;角色扮演档恒为默认值</summary>
    public EAgentPermissionMode Permission { get; init; }

    /// <summary>无人值守 shell 预授权模式(换行拼接);无则空串</summary>
    public string PreAuthorizedShellPatterns { get; init; } = string.Empty;

    /// <summary>文件工具开关</summary>
    public bool FileAccess { get; init; }

    /// <summary>shell 工具开关</summary>
    public bool Shell { get; init; }

    /// <summary>
    /// 受管 Python 环境是否就绪。它不是工具开关，而是 shell 纪律段的一个输入
    /// （见 <c>AgentToolPrompts.BuildPython</c>）——用户在设置页建完环境，
    /// 不重开会话也该让模型知道那个解释器在了。
    /// </summary>
    public bool PythonEnvReady { get; init; }

    /// <summary>网络搜索工具开关</summary>
    public bool WebSearch { get; init; }

    /// <summary>文件记忆(框架 FileMemoryProvider)开关</summary>
    public bool FileMemory { get; init; }

    /// <summary>定时任务工具开关</summary>
    public bool ScheduledTasks { get; init; }

    /// <summary>识图工具开关</summary>
    public bool VisionTool { get; init; }

    /// <summary>知识库检索工具开关</summary>
    public bool KnowledgeSearchTool { get; init; }

    /// <summary>子代理工具开关</summary>
    public bool SubAgent { get; init; }

    /// <summary>
    /// 挂载的子智能体名单（标识+名字+描述，换行拼接）。
    ///
    /// 名单连同各自的名字与描述在装配时被固化进子代理工具的花名册，
    /// 因此它属于装配输入。少了它的后果是：不关会话就改子智能体名单，
    /// 回来派活仍按旧名单走，而且改名改描述模型也看不见。
    /// </summary>
    public string MountedAgents { get; init; } = string.Empty;

    /// <summary>当前模型是否自带视觉(决定识图工具挂不挂;视觉↔非视觉切换经此触发重建)</summary>
    public bool ModelSupportsVision { get; init; }

    /// <summary>任务清单开关(框架 TodoProvider)</summary>
    public bool TodoList { get; init; }

    /// <summary>计划模式开关(框架 AgentModeProvider)</summary>
    public bool AgentMode { get; init; }

    /// <summary>工作区说明文件内容(AGENTS.md/CLAUDE.md);文件编辑经此触发重建</summary>
    public string WorkspaceInstructions { get; init; } = string.Empty;

    /// <summary>禁用的技能名(换行拼接);过滤在装配时固化,故属装配输入</summary>
    public string DisabledSkills { get; init; } = string.Empty;

    /// <summary>
    /// MCP 侧修订号(见 <see cref="McpManager.Revision"/>)。工具集与 server 自述<b>都</b>由它捕获——
    /// 自述随工具同一次取回、同一次自增，故不必再单列一个字段。
    /// 与工作区说明的差别在此：那个是磁盘文件，没有修订号可依，只能把内容本身入账。
    /// </summary>
    public int McpRevision { get; init; }

    /// <summary>禁用的 MCP server 名(换行拼接)；过滤在装配时固化，故属装配输入</summary>
    public string DisabledMcpServers { get; init; } = string.Empty;

    /// <summary>
    /// 从构建配置捕获事实（装配输入的常规入口）。
    /// 系统提示词在此重算——角色卡与会话参数的编辑因此天然被捕获。
    ///
    /// <b>与装配同一个入参</b>：两者都从 profile 出发，因此「装配读了什么」与
    /// 「重建判据比了什么」不可能各自漂移。这里曾经收的是 <c>ChatSession</c>，
    /// 与装配收的 profile 是两条路——子智能体名单就是那样漏掉的：
    /// 装配读它、快照不比它，于是改完名单不重建，回来仍按旧名单派活。
    /// </summary>
    /// <param name="profile">构建配置</param>
    /// <returns>装配事实</returns>
    public static AgentAssemblyFacts Capture(AgentBuildProfile profile)
    {
        CharacterData character = profile.Character;
        return Capture(character, CharacterPromptBuilder.Build(character, profile.PromptArguments),
            profile.WorkspacePath,
            profile.PermissionMode,
            profile.PreAuthorizedShellPatterns,
            McpManager.Instance.Revision,
            character.Kind.IsAgent()
                ? WorkspaceInstructionsLoader.Load(profile.WorkspacePath)
                : string.Empty,
            profile.ResolveCurrentModel()?.IsVisionModel == true,
            //与装配读的是同一个解析器,过滤规则不会两处漂移
            character.Kind.IsAgent() ? CharacterRunnerFactory.ResolveMountedAgents(character) : null,
            PythonEnvironment.IsReady);
    }

    /// <summary>
    /// 显式入参捕获快照(可单测,不触碰任何单例)
    /// </summary>
    /// <param name="character">角色</param>
    /// <param name="instructions">重算好的系统提示词</param>
    /// <param name="workspacePath">工作目录</param>
    /// <param name="permission">权限档</param>
    /// <param name="preAuthorizedShellPatterns">shell 预授权模式</param>
    /// <param name="mcpRevision">MCP 工具集修订号</param>
    /// <param name="workspaceInstructions">工作区说明文件内容</param>
    /// <param name="modelSupportsVision">当前模型是否自带视觉</param>
    /// <param name="mountedAgents">已解析的子智能体名单（过滤规则见 <c>CharacterRunnerFactory.ResolveMountedAgents</c>）</param>
    /// <param name="pythonEnvReady">受管 Python 环境是否已就绪</param>
    /// <returns>快照</returns>
    public static AgentAssemblyFacts Capture(CharacterData character,
        string instructions, string? workspacePath,
        EAgentPermissionMode permission, IReadOnlyList<string>? preAuthorizedShellPatterns,
        int mcpRevision, string workspaceInstructions = "",
        bool modelSupportsVision = false, IReadOnlyList<CharacterData>? mountedAgents = null,
        bool pythonEnvReady = false)
    {
        // 非智能体档不装配工具,工具相关输入一律归零——能力配置变化不连累它们重建
        bool isAgent = character.Kind.IsAgent();
        AgentToolConfig config = character.Tools;
        return new AgentAssemblyFacts
        {
            CharacterId = character.CharacterId,
            Kind = character.Kind,
            Instructions = instructions,
            ExecutionSettings = JsonSerializer.Serialize(character.Config.ExecutionSettings),
            WorkspacePath = isAgent ? workspacePath : null,
            Permission = isAgent ? permission : default,
            PreAuthorizedShellPatterns = isAgent && preAuthorizedShellPatterns is { Count: > 0 }
                ? string.Join('\n', preAuthorizedShellPatterns)
                : string.Empty,
            FileAccess = isAgent && config.EnableFileAccess,
            Shell = isAgent && config.EnableShellExecution,
            //只在挂了 shell 时入账:没 shell 就没人跑得动它,纪律段本来也不发
            PythonEnvReady = isAgent && config.EnableShellExecution && pythonEnvReady,
            WebSearch = isAgent && config.EnableWebSearch,
            FileMemory = isAgent && config.EnableFileMemory,
            ScheduledTasks = isAgent && config.EnableScheduledTasks,
            VisionTool = isAgent && config.EnableVisionTool,
            KnowledgeSearchTool = isAgent && config.EnableKnowledgeSearchTool,
            SubAgent = isAgent && config.EnableSubAgent,
            // 名字与描述一并入账:花名册固化的正是这两样,改名改描述模型也该重新看见
            MountedAgents = isAgent && config.EnableSubAgent && mountedAgents is { Count: > 0 }
                ? string.Join('\n',
                    mountedAgents.Select(x => $"{x.CharacterId}\t{x.CharacterName}\t{x.Description}"))
                : string.Empty,
            ModelSupportsVision = isAgent && modelSupportsVision,
            TodoList = isAgent && config.EnableTodoList,
            AgentMode = isAgent && config.EnableAgentMode,
            WorkspaceInstructions = isAgent ? workspaceInstructions : string.Empty,
            DisabledSkills = isAgent ? string.Join('\n', config.DisabledSkills) : string.Empty,
            McpRevision = isAgent ? mcpRevision : 0,
            DisabledMcpServers = isAgent ? string.Join('\n', config.DisabledMcpServers) : string.Empty,
        };
    }
}
