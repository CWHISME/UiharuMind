/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
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
    /// <summary>
    /// 驱动整个装配的角色：<see cref="CharacterData.Kind"/> 决定是否装配工具与工作目录，
    /// Template 与挂载列表决定系统提示，<see cref="CharacterData.MountAgents"/> 决定子 agent。
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
}

/// <summary>
/// 一个构建完成的 agent 及宿主需要的配套句柄
/// </summary>
public sealed class AgentHandle : IAsyncDisposable
{
    /// <summary>Harness agent(标准 AIAgent 契约)</summary>
    public AIAgent Agent { get; }

    private readonly ShellExecutor? _shellExecutor;

    /// <summary>todo 提供器(侧栏进度)</summary>
    public TodoProvider? Todos => Agent.GetService<TodoProvider>();

    /// <summary>plan/execute 模式提供器</summary>
    public AgentModeProvider? Mode => Agent.GetService<AgentModeProvider>();

    /// <summary>运行中插话通道</summary>
    public MessageInjectingChatClient? MessageInjector => Agent.GetService<MessageInjectingChatClient>();

    public AgentHandle(AIAgent agent, ShellExecutor? shellExecutor)
    {
        Agent = agent;
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

    /// <summary>识图工具名(纪律段里要指名道姓地告诉模型可以用它)</summary>
    private const string VisionToolName = "ask_vision";

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
    /// <returns>执行者;使用前需先调用 <see cref="ICharacterRunner.AttachAsync"/></returns>
    public ICharacterRunner CreateRunner()
    {
        return new HarnessCharacterRunner();
    }

    /// <summary>
    /// 按配置构建一个 HarnessAgent。角色扮演与 agent 走同一个引擎，
    /// 差异全部落在 HarnessAgentOptions 上：
    /// 角色扮演档把框架的每一项能力都关掉、工具集为空、HarnessInstructions 为空串，
    /// 使框架不向系统提示里添加任何内容——等价于一次纯聊天调用，外加白拿的运行中插话能力。
    /// </summary>
    /// <param name="profile">构建配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>agent 句柄</returns>
    public async Task<AgentHandle> CreateAgentAsync(AgentBuildProfile profile,
        CancellationToken cancellationToken = default)
    {
        IChatClient client = new LazyChatClient(profile.SessionModelSource);
        // 历史落到自有会话文件,框架 blob 里只剩 todos/mode/审批与一个会话标识指针
        SessionChatHistoryProvider history = new();
        CharacterData character = profile.Character;

        // 角色自身的提示词(挂载片段 + Template + 对话模板)始终作为 ChatOptions.Instructions;
        // 框架会把 HarnessInstructions 拼在它前面
        ChatOptions chatOptions = character.Config.ExecutionSettings.ToChatOptions();
        chatOptions.Instructions = CharacterPromptBuilder.Build(character, profile.PromptArguments);

        List<AIContextProvider> contextProviders = [new MemoryContextProvider()];

        if (character.Kind == ECharacterKind.Roleplay)
        {
            return BuildHandle(client, new HarnessAgentOptions
            {
                Name = SanitizeAgentName(character.CharacterName, character.CharacterId),
                Description = character.Description,
                ChatHistoryProvider = history,
                // 框架侧一律关闭:任何一项漏关都会向角色扮演的上下文里注入内容
                HarnessInstructions = string.Empty,
                DisableWebSearch = true,
                DisableFileAccess = true,
                DisableFileMemory = true,
                DisableTodoProvider = true,
                DisableAgentModeProvider = true,
                DisableAgentSkillsProvider = true,
                DisableCompaction = true,
                DisableToolAutoApproval = true,
                DisableOpenTelemetry = true,
                AIContextProviders = contextProviders,
                ChatOptions = chatOptions,
            }, null);
        }

        AgentSettingConfig config = AgentSettingConfig.Current;
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

        chatOptions.Tools = extraTools;

        return BuildHandle(client, new HarnessAgentOptions
        {
            Name = SanitizeAgentName(character.CharacterName, character.CharacterId),
            Description = character.Description,
            ChatHistoryProvider = history,
            // 工具纪律段随实际装配的工具集派生;角色的人格/任务段由框架拼在其后
            HarnessInstructions = BuildToolDisciplines(config, shellExecutor != null),
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
            BackgroundAgents = BuildBackgroundAgents(client,
                character.MountAgents.Count > 0 ? character.MountAgents : DefaultMountedAgentIds),
            AIContextProviders = contextProviders,
            ToolApprovalAgentOptions = new ToolApprovalAgentOptions
            {
                AutoApprovalRules = ApprovalModeMapper.BuildRules(profile.PermissionMode,
                    profile.PreAuthorizedShellPatterns),
            },
            ChatOptions = chatOptions,
        }, shellExecutor);
    }

    private static AgentHandle BuildHandle(IChatClient client, HarnessAgentOptions options,
        ShellExecutor? shellExecutor)
    {
        // 将插件库内部日志(含工具执行失败的真实异常)转发到 UiharuMind 日志
        MfaLoggerFactory loggerFactory = new();
        IServiceProvider services = new MfaServiceProvider(loggerFactory);
        return new AgentHandle(client.AsHarnessAgent(options, loggerFactory, services), shellExecutor);
    }

    private static string GetScratchDirectory()
    {
        string path = Path.Combine(SettingConfig.SaveAgentDataPath, "Scratch");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// 工具纪律段：按<b>实际装配的工具集</b>派生，而不是一段固定文本。
    /// 这些内容不是人格而是"这套工具怎么用才不出错"的约束，与工具是否启用强耦合——
    /// 用户关掉文件工具后还讲 Glob/Read/Edit 就是纯噪声，因此归属是代码而非角色卡。
    /// 角色自身的人格/任务段由框架拼在本段之后。
    /// </summary>
    /// <param name="config">agent 能力配置</param>
    /// <param name="hasShell">是否装配了 shell</param>
    /// <returns>纪律段文本</returns>
    private static string BuildToolDisciplines(AgentSettingConfig config, bool hasShell)
    {
        StringBuilder sb = new();
        sb.AppendLine("# Identity");
        sb.Append("You are the Workspace Agent of UiharuMind.");
        if (config.EnableFileAccess)
        {
            sb.Append(" You manage the local filesystem primarily through tools(Glob/Grep/Read/Edit)");
            sb.Append(hasShell ? ", and fall back to shell only for non-file system interactions." : ".");
        }
        else if (hasShell)
        {
            sb.Append(" You interact with the local machine through the shell tool.");
        }

        sb.AppendLine();

        if (config.EnableFileAccess)
        {
            sb.AppendLine();
            sb.AppendLine("# Path Discipline");
            sb.AppendLine("- **No assumed root.** Derive all paths from the user's latest request or prior tool outputs.");
            sb.AppendLine("- Every call must pass an explicit `path` parameter. If the scope is ambiguous, resolve it with one `Glob` call rather than asking the user.");
            sb.AppendLine();
            sb.AppendLine("# Edit Discipline");
            sb.AppendLine("- Read before overwrite. Inspect surrounding context so diffs stay minimal and reversible.");
            sb.AppendLine("- Never emit full-file rewrites for single-line changes.");
        }

        sb.AppendLine();
        sb.AppendLine("# Images");
        sb.AppendLine($"- Attachments arrive as `[Attached file: <path>]`. When the path is an image and you need to know what it shows, call `{VisionToolName}` with that path — do not guess from the file name.");

        sb.AppendLine();
        sb.AppendLine("# Execution Modes");
        sb.AppendLine("- **Interactive:** Present when the user is at their desk. Confirm only truly destructive deletions.");
        sb.Append("- **Headless (Scheduled/Triggers):** If invoked without a live user session, **run fully autonomously**. Log results via tool output; never block on clarification or confirmation.");

        return sb.ToString();
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
