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
using UiharuMind.Core.AI.Agent.Harness;
using UiharuMind.Core.AI.Agent.Mcp;
using UiharuMind.Core.AI.Agent.Scheduler;
using UiharuMind.Core.AI.Agent.Skills;
using UiharuMind.Core.AI.Agent.Tools.WebTools;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Memory;
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
    /// 会话级记忆库来源(memory_search 工具执行时解析,锁定当前挂接会话的单库)
    /// </summary>
    public Func<MemoryData?>? SessionMemorySource { get; init; }

    /// <summary>
    /// 会话级 shell 放行模式来源(审批规则每次执行时解析,
    /// 用户点"记住同类命令"后立即生效,无需重建装配)
    /// </summary>
    public Func<IReadOnlyList<string>?>? SessionShellApprovalSource { get; init; }
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

    /// <summary>记忆检索工具名(纪律段引用)</summary>
    private const string MemoryToolName = "memory_search";

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
    ///
    /// 装配是纯同步的内存组装：MCP 工具取常驻缓存(见 <see cref="Mcp.McpManager.GetCachedTools"/>)，
    /// 绝不等待网络。重建时机由 <see cref="AgentAssemblySnapshot"/> 差异决定。
    /// </summary>
    /// <param name="profile">构建配置</param>
    /// <returns>agent 句柄</returns>
    public AgentHandle CreateAgent(AgentBuildProfile profile)
    {
        IChatClient client = new LazyChatClient(profile.SessionModelSource);
        // 历史落到自有会话文件,框架 blob 里只剩 todos/mode/审批与一个会话标识指针
        SessionChatHistoryProvider history = new();
        CharacterData character = profile.Character;
        AgentSettingConfig config = AgentSettingConfig.Current;

        // 角色自身的提示词(Template + 对话模板)始终作为 ChatOptions.Instructions;
        // 框架会把 HarnessInstructions 拼在它前面
        ChatOptions chatOptions = character.Config.ExecutionSettings.ToChatOptions();
        chatOptions.Instructions = CharacterPromptBuilder.Build(character, profile.PromptArguments);

        List<AIContextProvider> contextProviders =
        [
            new MemoryContextProvider(hasMemoryTool:
                character.Kind == ECharacterKind.Agent && config.EnableMemorySearchTool),
        ];

        if (character.Kind == ECharacterKind.Roleplay)
        {
            return BuildHandle(client,
                BuildRoleplayOptions(character, history, contextProviders, chatOptions), null);
        }

        string workingDirectory = profile.WorkspacePath ?? GetScratchDirectory();
        LocalShellExecutor? shellExecutor = null;
        if (config.EnableShellExecution)
        {
            shellExecutor = new(new LocalShellExecutorOptions
            {
                WorkingDirectory = workingDirectory,
            });
        }

        List<AITool> extraTools = new();
        if (shellExecutor != null)
        {
            // 1.16:shell 作为普通工具挂载,默认名即 run_shell、默认自包审批,预授权规则按名匹配不变
            extraTools.Add(shellExecutor.AsAIFunction());
        }

        if (config.EnableVisionTool)
        {
            extraTools.Add(VisionTool.Create());
        }

        if (config.EnableMemorySearchTool)
        {
            extraTools.Add(MemoryTool.Create(profile.SessionMemorySource));
        }

        if (config.EnableScheduledTasks)
        {
            extraTools.Add(SchedulerTools.CreateScheduledTaskTool(profile.WorkspacePath));
        }

        extraTools.AddRange(McpManager.Instance.GetCachedTools());

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

        FileSystemAgentFileStore? agentNotesStore = config.EnableAgentNotes
            ? new FileSystemAgentFileStore(Path.Combine(SettingConfig.SaveAgentDataPath, "FileMemory"))
            : null;

        AIAgent? researcher = BuildResearcherAgent(client, config, workingDirectory);

        return BuildHandle(client, BuildAgentOptions(character, config, history, contextProviders, chatOptions,
            SkillCatalog.Instance.BuildSkillsSource(), agentNotesStore,
            profile.PermissionMode, profile.PreAuthorizedShellPatterns,
            researcher == null ? null : [researcher], profile.SessionShellApprovalSource,
            WorkspaceInstructionsLoader.Load(profile.WorkspacePath)), shellExecutor);
    }

    /// <summary>
    /// 内置"调研员"只读子代理:主 agent 可把大范围的探查/调研任务委托给它,
    /// 结论以报告返回,不吃主上下文。零配置面——工具集尊重全局能力开关,
    /// 全部只读能力都被关掉时不挂载。
    /// 只读因而免审批,不涉及嵌套审批通道;无 shell/写/技能/todo/mode,一次性会话不落盘。
    /// </summary>
    /// <param name="client">模型客户端(与主 agent 同一惰性客户端)</param>
    /// <param name="config">能力配置</param>
    /// <param name="workingDirectory">工作目录(只读文件工具的根)</param>
    /// <returns>子代理;无任何只读能力可用时为 null</returns>
    private static AIAgent? BuildResearcherAgent(IChatClient client, AgentSettingConfig config,
        string workingDirectory)
    {
        HarnessAgentOptions? options = BuildResearcherOptions(config, workingDirectory);
        if (options == null) return null;

        MfaLoggerFactory loggerFactory = new();
        return client.AsHarnessAgent(options, loggerFactory, new MfaServiceProvider(loggerFactory));
    }

    /// <summary>
    /// 调研员装配选项(纯函数,不碰单例)。不变量:工具集只含只读工具——
    /// 它在主 agent 的工具调用内部无头运行,没有审批通道,任何可变更工具混入都是越权。
    /// </summary>
    /// <param name="config">能力配置</param>
    /// <param name="workingDirectory">只读文件工具的根目录</param>
    /// <returns>框架选项;无任何只读能力启用时为 null</returns>
    internal static HarnessAgentOptions? BuildResearcherOptions(AgentSettingConfig config,
        string workingDirectory)
    {
        List<AITool> tools = new();
        if (config.EnableFileAccess)
        {
            tools.AddRange(new PermissiveFileAccessTools(workingDirectory).Create(disableWriteTools: true));
        }

        if (config.EnableWebSearch)
        {
            tools.Add(WebSearchTool.Create());
            tools.Add(WebFetchTool.Create());
        }

        if (config.EnableVisionTool)
        {
            tools.Add(VisionTool.Create());
        }

        if (tools.Count == 0) return null;

        return new HarnessAgentOptions
        {
            Name = "Researcher",
            Description = "Read-only researcher: surveys the workspace files and/or the web, inspects images, " +
                          "and returns a focused report. Delegate broad exploration or research tasks to it " +
                          "to keep the main context small. It cannot modify anything.",
            HarnessInstructions = string.Empty,
            // 与角色扮演档同一原则:框架有状态能力全关,子代理是一次性的纯工具循环
            // (1.16 起框架文件工具只随 FileAccessStore 出现,不设即无,无需显式关闭)
            DisableWebSearch = true,
            DisableFileMemory = true,
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = true,
            DisableCompaction = true,
            DisableToolAutoApproval = true,
            DisableOpenTelemetry = true,
            ChatOptions = new ChatOptions
            {
                Instructions = BuildResearcherInstructions(config),
                Tools = tools,
            },
        };
    }

    /// <summary>
    /// 调研员的系统提示:身份 + 只读边界 + 报告体例,按启用的能力裁剪。
    /// </summary>
    /// <param name="config">能力配置</param>
    /// <returns>提示词</returns>
    private static string BuildResearcherInstructions(AgentSettingConfig config)
    {
        StringBuilder sb = new();
        sb.AppendLine("# Role");
        sb.AppendLine("You are the Researcher, a read-only sub-agent of UiharuMind. " +
                      "You investigate and report; you never modify anything and you have no shell.");
        sb.AppendLine();
        sb.AppendLine("# Method");
        if (config.EnableFileAccess)
        {
            sb.AppendLine("- Explore workspace files with Glob/Grep/Read. Pass explicit paths.");
        }

        if (config.EnableWebSearch)
        {
            sb.AppendLine("- Research the web with web_search, then web_fetch the promising results.");
        }

        if (config.EnableVisionTool)
        {
            sb.AppendLine($"- For image files, call `{VisionToolName}` with the file path.");
        }

        sb.AppendLine("- Work autonomously; never ask for clarification.");
        sb.Append("- Return a focused report: conclusions first, then the evidence (paths, URLs, quotes).");
        return sb.ToString();
    }

    // [MFA绕坑] 绕:框架默认向系统提示注入自身内容 因:无"纯透传"档,只能逐项 Disable 删除条件:框架提供 passthrough 模式
    /// <summary>
    /// 角色扮演档选项(纯函数,不碰单例)。不变量:框架侧一律关闭、HarnessInstructions 为空——
    /// 任何一项漏关都会向角色扮演的上下文里注入内容,该不变量由测试钉住。
    /// </summary>
    /// <param name="character">角色</param>
    /// <param name="history">历史提供器</param>
    /// <param name="contextProviders">上下文提供器</param>
    /// <param name="chatOptions">对话选项(含角色系统提示,工具应为空)</param>
    /// <returns>框架选项</returns>
    internal static HarnessAgentOptions BuildRoleplayOptions(CharacterData character,
        ChatHistoryProvider history, List<AIContextProvider> contextProviders, ChatOptions chatOptions)
    {
        return new HarnessAgentOptions
        {
            Name = SanitizeAgentName(character.CharacterName, character.CharacterId),
            Description = character.Description,
            ChatHistoryProvider = history,
            HarnessInstructions = string.Empty,
            DisableWebSearch = true,
            DisableFileMemory = true,
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = true,
            DisableCompaction = true,
            DisableToolAutoApproval = true,
            DisableOpenTelemetry = true,
            AIContextProviders = contextProviders,
            ChatOptions = chatOptions,
        };
    }

    /// <summary>
    /// agent 档选项(纯函数,不碰单例)。工具纪律段随实际装配的工具集派生,
    /// 角色的人格/任务段由框架拼在其后;框架自带的搜索/文件访问关闭,由自装配工具替代。
    /// </summary>
    /// <param name="character">角色</param>
    /// <param name="config">能力配置</param>
    /// <param name="history">历史提供器</param>
    /// <param name="contextProviders">上下文提供器</param>
    /// <param name="chatOptions">对话选项(含角色系统提示与已装配工具集,shell 工具已在其中)</param>
    /// <param name="skillsSource">技能来源</param>
    /// <param name="agentNotesStore">agent 笔记存储,禁用时为 null</param>
    /// <param name="permissionMode">权限档</param>
    /// <param name="preAuthorizedShellPatterns">无人值守 shell 预授权模式</param>
    /// <param name="backgroundAgents">背景子代理(内置调研员),无则 null</param>
    /// <param name="sessionShellApprovalSource">会话级 shell 放行模式来源,可空</param>
    /// <param name="workspaceInstructions">工作区说明文件内容,无则空串</param>
    /// <returns>框架选项</returns>
    internal static HarnessAgentOptions BuildAgentOptions(CharacterData character, AgentSettingConfig config,
        ChatHistoryProvider history, List<AIContextProvider> contextProviders, ChatOptions chatOptions,
        AgentSkillsSource skillsSource, FileSystemAgentFileStore? agentNotesStore,
        EAgentPermissionMode permissionMode, IReadOnlyList<string>? preAuthorizedShellPatterns,
        List<AIAgent>? backgroundAgents = null,
        Func<IReadOnlyList<string>?>? sessionShellApprovalSource = null,
        string workspaceInstructions = "")
    {
        return new HarnessAgentOptions
        {
            Name = SanitizeAgentName(character.CharacterName, character.CharacterId),
            Description = character.Description,
            ChatHistoryProvider = history,
            HarnessInstructions = BuildToolDisciplines(config, workspaceInstructions: workspaceInstructions),
            DisableWebSearch = true,
            DisableOpenTelemetry = true,
            FileMemoryStore = agentNotesStore,
            // 1.16:框架文件工具只随 FileAccessStore 出现;shell 改为普通工具挂在 ChatOptions.Tools
            FileAccessStore = null,
            AgentSkillsSource = skillsSource,
            BackgroundAgents = backgroundAgents,
            AIContextProviders = contextProviders,
            ToolApprovalAgentOptions = new ToolApprovalAgentOptions
            {
                AutoApprovalRules = ApprovalModeMapper.BuildRules(permissionMode, preAuthorizedShellPatterns,
                    sessionShellApprovalSource),
            },
            ChatOptions = chatOptions,
        };
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
    /// <param name="workspaceInstructions">工作区说明文件内容,无则空串</param>
    /// <returns>纪律段文本</returns>
    private static string BuildToolDisciplines(AgentSettingConfig config,string workspaceInstructions = "")
    {
        StringBuilder sb = new();
        if (config.EnableFileAccess)
        {
            sb.AppendLine("# Identity");
            sb.Append(" You manage the local filesystem primarily through tools(Glob/Grep/Read/Edit)");
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

        // 纪律段严格随门控派生:关掉的工具绝不出现在提示词里,否则是纯噪声
        if (config.EnableVisionTool)
        {
            sb.AppendLine();
            sb.AppendLine("# Images");
            sb.AppendLine($"- Attachments arrive as `[Attached file: <path>]`. When the path is an image and you need to know what it shows, call `{VisionToolName}` with that path — do not guess from the file name.");
        }

        if (config.EnableMemorySearchTool)
        {
            sb.AppendLine();
            sb.AppendLine("# Memory Recall");
            sb.AppendLine($"- A long-term memory library may be bound to this session. When past context matters, call `{MemoryToolName}` with a focused query instead of guessing; it returns relevant snippets or reports that no library is bound.");
        }
        
        if (!string.IsNullOrEmpty(workspaceInstructions))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("# Workspace Instructions (from the project's AGENTS.md)");
            sb.AppendLine("Follow these project-specific rules while working in this workspace:");
            sb.Append(workspaceInstructions);
        }

        return sb.ToString();
    }

    private static string SanitizeAgentName(string displayName, string fallback)
    {
        string name = new(displayName.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(name) ? fallback : name;
    }
}
