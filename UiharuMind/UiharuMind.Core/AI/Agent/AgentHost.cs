/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Agent.Files;
using UiharuMind.Core.AI.Agent.Mcp;
using UiharuMind.Core.AI.Agent.Scheduler;
using UiharuMind.Core.AI.Agent.Skills;
using UiharuMind.Core.AI.Agent.Tools.WebTools;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// 构建 HarnessAgent 的配置
/// </summary>
public class AgentBuildProfile
{
    /// <summary>绑定的工作目录;为空表示通用助手模式(文件/shell 工具落到沙箱目录)</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>权限档</summary>
    public EAgentPermissionMode PermissionMode { get; init; } = EAgentPermissionMode.AutoEdit;

    /// <summary>预授权 shell 命令模式(定时任务无人值守用)</summary>
    public IReadOnlyList<string>? PreAuthorizedShellPatterns { get; init; }

    /// <summary>
    /// 委托挂载的子 agent 角色标识。null 表示用 <see cref="AgentHost.DefaultMountedAgentIds"/>；
    /// 阶段 3 起改由 agent 角色自身的 <see cref="CharacterData.MountAgents"/> 提供。
    /// </summary>
    public IReadOnlyList<string>? MountedAgentIds { get; init; }
}

/// <summary>
/// 一个构建完成的 agent 及宿主需要的配套句柄
/// </summary>
public sealed class AgentHandle : IAsyncDisposable
{
    /// <summary>Harness agent(标准 AIAgent 契约)</summary>
    public AIAgent Agent { get; }

    /// <summary>聊天历史提供器(消息存于 session StateBag,回放用)</summary>
    public InMemoryChatHistoryProvider History { get; }

    private readonly ShellExecutor? _shellExecutor;

    /// <summary>todo 提供器(侧栏进度)</summary>
    public TodoProvider? Todos => Agent.GetService<TodoProvider>();

    /// <summary>plan/execute 模式提供器</summary>
    public AgentModeProvider? Mode => Agent.GetService<AgentModeProvider>();

    /// <summary>运行中插话通道</summary>
    public MessageInjectingChatClient? MessageInjector => Agent.GetService<MessageInjectingChatClient>();

    public AgentHandle(AIAgent agent, InMemoryChatHistoryProvider history, ShellExecutor? shellExecutor)
    {
        Agent = agent;
        History = history;
        _shellExecutor = shellExecutor;
    }

    public async ValueTask DisposeAsync()
    {
        if (_shellExecutor != null) await _shellExecutor.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Agent 子系统宿主:基于 Microsoft.Agents.AI Harness 组装 agent,
/// 聚合技能目录、MCP 工具、识图子能力与定时调度(框架缺失的唯一自建件)。
/// </summary>
public class AgentHost : Singleton<AgentHost>, IInitialize
{
    /// <summary>shell 工具名(供预授权规则匹配)</summary>
    public const string ShellToolName = "run_shell";

    /// <summary>
    /// 未指定委托挂载时的默认子 agent：识图助手。
    /// 取代了原先内置的 vision AgentProfile —— 同一能力现在就是一个普通角色。
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultMountedAgentIds =
        [nameof(DefaultCharacter.Vision)];

    /// <summary>定时任务调度后端(框架无对应能力,自建保留)</summary>
    public ISchedulerBackend Scheduler { get; private set; } = null!;

    public void OnInitialize()
    {
        SkillCatalog.Instance.EnsureDemoSkill();
        Scheduler = new InProcessSchedulerBackend();
    }

    /// <summary>
    /// 创建一个对话执行者。框架类型止步于实现内部,调用方只见稳定类型。
    /// </summary>
    /// <returns>执行者;使用前需先调用 <see cref="ICharacterRunner.ConfigureAsync"/></returns>
    public ICharacterRunner CreateRunner()
    {
        return new HarnessCharacterRunner();
    }

    /// <summary>
    /// 按配置构建一个 HarnessAgent
    /// </summary>
    /// <param name="profile">构建配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>agent 句柄</returns>
    public async Task<AgentHandle> CreateAgentAsync(AgentBuildProfile profile,
        CancellationToken cancellationToken = default)
    {
        var config = AgentSettingConfig.Current;

        IChatClient client = new LazyChatClient();
        InMemoryChatHistoryProvider history = new();

        string workingDirectory = profile.WorkspacePath ?? GetScratchDirectory();
        LocalShellExecutor? shellExecutor = null;
        if (config.EnableShellExecution)
        {
            shellExecutor = new(new LocalShellExecutorOptions
            {
                WorkingDirectory = workingDirectory,
            });
        }

        List<AITool> extraTools = new()
        {
            VisionTool.Create(),
        };
        if (config.EnableTodo)
        {
            extraTools.Add(SchedulerTools.CreateScheduledTaskTool(profile.WorkspacePath));
        }
        extraTools.AddRange(await McpManager.Instance.GetToolsAsync(cancellationToken).ConfigureAwait(false));

        if (config.EnableFileAccess)
        {
            extraTools.AddRange(new PermissiveFileAccessTools(workingDirectory).Create());
        }

        if (config.EnableWebSearch)
        {
            extraTools.Add(WebSearchTool.Create());
            extraTools.Add(WebFetchTool.Create());
        }

        HarnessAgentOptions options = new()
        {
            Name = "UiharuAgent",
            Description = "UiharuMind workspace agent.",
            ChatHistoryProvider = history,
            DisableWebSearch = true,
            DisableOpenTelemetry = true,
            DisableFileAccess = true,
            FileMemoryStore = config.EnableMemory
                ? new FileSystemAgentFileStore(Path.Combine(SettingConfig.SaveAgentDataPath, "FileMemory"))
                : null,
            FileAccessStore = null,
            ShellExecutor = shellExecutor,
            ShellToolName = ShellToolName,
            AgentSkillsSource = SkillCatalog.Instance.BuildSkillsSource(),
            BackgroundAgents = BuildBackgroundAgents(client, profile.MountedAgentIds ?? DefaultMountedAgentIds),
            ToolApprovalAgentOptions = new ToolApprovalAgentOptions
            {
                AutoApprovalRules = ApprovalModeMapper.BuildRules(profile.PermissionMode,
                    profile.PreAuthorizedShellPatterns),
            },
            ChatOptions = new ChatOptions
            {
                Instructions = BuildInstructions(profile),
                Tools = extraTools,
            },
        };

        // 将插件库内部日志(含工具执行失败的真实异常)转发到 UiharuMind 日志
        MfaLoggerFactory loggerFactory = new();
        IServiceProvider services = new MfaServiceProvider(loggerFactory);

        return new AgentHandle(client.AsHarnessAgent(options, loggerFactory, services), history, shellExecutor);
    }

    private static string GetScratchDirectory()
    {
        string path = Path.Combine(SettingConfig.SaveAgentDataPath, "Scratch");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }

    private static string BuildInstructions(AgentBuildProfile profile)
    {
        return
            $"""
             # Identity
             You are the Workspace Agent of UiharuMind. You manage the local filesystem primarily through tools(Glob/Grep/Read/Edit), and fall back to shell only for non-file system interactions.
             
             # Path Discipline
             - **No assumed root.** Derive all paths from the user's latest request or prior tool outputs.
             - Every call must pass an explicit `path` parameter. If the scope is ambiguous, resolve it with one `Glob` call rather than asking the user.
             
             # Edit Discipline
             - Read before overwrite. Inspect surrounding context so diffs stay minimal and reversible.
             - Never emit full-file rewrites for single-line changes.
             
             # Execution Modes
             - **Interactive:** Present when the user is at their desk. Confirm only truly destructive deletions.
             - **Headless (Scheduled/Triggers):** If invoked without a live user session, **run fully autonomously**. Log results via tool output; never block on clarification or confirmation.
             """;
    }

    /// <summary>
    /// 把委托挂载的角色组装为后台子 agent(识图等专职助手)。
    /// 主 agent 依据各子 agent 的 Description 自主决定何时委托。
    /// </summary>
    private static List<AIAgent> BuildBackgroundAgents(IChatClient defaultClient, IReadOnlyList<string> characterIds)
    {
        List<AIAgent> agents = new();
        foreach (string characterId in characterIds)
        {
            CharacterData character = CharacterManager.Instance.GetCharacterData(characterId);

            // Agent 类角色作子 agent 需要嵌套一层 Harness(工具集、审批链、workspace 全要再套),本次不支持
            if (character.Kind == ECharacterKind.Agent)
            {
                Log.Warning($"Skip background agent '{characterId}': nesting an agent character is not supported.");
                continue;
            }

            try
            {
                agents.Add(new ChatClientAgent(defaultClient,
                    instructions: CharacterPromptBuilder.Build(character),
                    name: SanitizeAgentName(character.CharacterName, character.CharacterId),
                    description: character.Description));
            }
            catch (Exception e)
            {
                Log.Warning($"Skip background agent '{characterId}': {e.Message}");
            }
        }

        return agents;
    }

    private static string SanitizeAgentName(string displayName, string fallback)
    {
        string name = new(displayName.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(name) ? fallback : name;
    }
}
