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

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 一次 agent 装配所消费的全部输入的快照（record 值相等比较）。
/// 每次挂接重算，与上次不等即重建装配——取代按字符串指纹"猜"重建时机：
/// 角色卡编辑、会话参数、能力开关、MCP 工具集变化都被直接捕获。
///
/// 字段列表同时就是装配依赖的显式清单。不在其列的：
/// 模型（惰性客户端按请求解析，切换无需重建）、记忆库（按请求经闭包解析）、
/// 技能文件内容（框架运行期从磁盘读取，天然是活的）。
/// </summary>
public sealed record AgentAssemblySnapshot
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

    /// <summary>网络搜索工具开关</summary>
    public bool WebSearch { get; init; }

    /// <summary>agent 笔记(框架文件记忆)开关</summary>
    public bool AgentNotes { get; init; }

    /// <summary>定时任务工具开关</summary>
    public bool ScheduledTasks { get; init; }

    /// <summary>识图工具开关</summary>
    public bool VisionTool { get; init; }

    /// <summary>记忆检索工具开关</summary>
    public bool MemorySearchTool { get; init; }

    /// <summary>子代理工具开关</summary>
    public bool SubAgent { get; init; }

    /// <summary>当前模型是否自带视觉(决定识图工具挂不挂;视觉↔非视觉切换经此触发重建)</summary>
    public bool ModelSupportsVision { get; init; }

    /// <summary>任务清单开关(框架 TodoProvider)</summary>
    public bool TodoList { get; init; }

    /// <summary>计划模式开关(框架 AgentModeProvider)</summary>
    public bool AgentMode { get; init; }

    /// <summary>工具纪律段的自定义提示词覆盖(拼接;设置页编辑经此触发重建)</summary>
    public string ToolPromptOverrides { get; init; } = string.Empty;

    /// <summary>工作区说明文件内容(AGENTS.md/CLAUDE.md);文件编辑经此触发重建</summary>
    public string WorkspaceInstructions { get; init; } = string.Empty;

    /// <summary>禁用的技能名(换行拼接);过滤在装配时固化,故属装配输入</summary>
    public string DisabledSkills { get; init; } = string.Empty;

    /// <summary>MCP 工具集修订号(见 <see cref="McpManager.Revision"/>)</summary>
    public int McpRevision { get; init; }

    /// <summary>
    /// 从会话捕获快照(装配输入的常规入口)。
    /// 系统提示词在此重算——角色卡与会话参数的编辑因此天然被捕获。
    /// </summary>
    /// <param name="session">目标会话</param>
    /// <returns>快照</returns>
    public static AgentAssemblySnapshot Capture(ChatSession session)
    {
        CharacterData character = session.CharacterData;
        return Capture(character, CharacterPromptBuilder.Build(character, session.CustomParams),
            session.WorkspacePath,
            (EAgentPermissionMode)Math.Clamp(session.PermissionModeIndex, 0, 2),
            session.PreAuthorizedShellPatterns,
            AgentSettingConfig.Current, McpManager.Instance.Revision,
            character.Kind == ECharacterKind.Agent
                ? WorkspaceInstructionsLoader.Load(session.WorkspacePath)
                : string.Empty,
            // 与 LazyChatClient 同一解析次序:会话绑定模型优先,回落全局当前模型
            session.ChatModelRunningData?.IsVisionModel == true);
    }

    /// <summary>
    /// 显式入参捕获快照(可单测,不触碰任何单例)
    /// </summary>
    /// <param name="character">角色</param>
    /// <param name="instructions">重算好的系统提示词</param>
    /// <param name="workspacePath">工作目录</param>
    /// <param name="permission">权限档</param>
    /// <param name="preAuthorizedShellPatterns">shell 预授权模式</param>
    /// <param name="config">agent 能力配置</param>
    /// <param name="mcpRevision">MCP 工具集修订号</param>
    /// <param name="workspaceInstructions">工作区说明文件内容</param>
    /// <param name="modelSupportsVision">当前模型是否自带视觉</param>
    /// <returns>快照</returns>
    public static AgentAssemblySnapshot Capture(CharacterData character,
        string instructions, string? workspacePath,
        EAgentPermissionMode permission, IReadOnlyList<string>? preAuthorizedShellPatterns,
        AgentSettingConfig config, int mcpRevision, string workspaceInstructions = "",
        bool modelSupportsVision = false)
    {
        // 角色扮演档不装配工具,工具相关输入一律归零——agent 侧配置变化不连累角色扮演重建
        bool isAgent = character.Kind == ECharacterKind.Agent;
        return new AgentAssemblySnapshot
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
            WebSearch = isAgent && config.EnableWebSearch,
            AgentNotes = isAgent && config.EnableAgentNotes,
            ScheduledTasks = isAgent && config.EnableScheduledTasks,
            VisionTool = isAgent && config.EnableVisionTool,
            MemorySearchTool = isAgent && config.EnableMemorySearchTool,
            SubAgent = isAgent && config.EnableSubAgent,
            ModelSupportsVision = isAgent && modelSupportsVision,
            TodoList = isAgent && config.EnableTodoList,
            AgentMode = isAgent && config.EnableAgentMode,
            ToolPromptOverrides = isAgent
                ? string.Join('\x1F', config.FileAccessPrompt, config.VisionToolPrompt, config.MemorySearchPrompt,
                    config.SubAgentPrompt)
                : string.Empty,
            WorkspaceInstructions = isAgent ? workspaceInstructions : string.Empty,
            DisabledSkills = isAgent ? string.Join('\n', config.DisabledSkills) : string.Empty,
            McpRevision = isAgent ? mcpRevision : 0,
        };
    }
}
