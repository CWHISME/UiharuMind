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
using Microsoft.Agents.AI.Compaction;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Harness;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Scheduler;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.WebTools;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Execution;

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

    /// <summary>
    /// 本会话装配好的对话选项（系统提示词、工具集与采样参数）。
    ///
    /// 旁路请求（如写交接文档）必须带上同一份：请求体的前缀是「system + 工具定义 + 消息」，
    /// 少任何一段，前缀都会从那里开始与常规轮次岔开，服务端的前缀缓存整个作废——
    /// 而旁路的那一发恰好是占用最高时发出的、最大的一次请求。
    /// </summary>
    public ChatOptions? ChatOptions { get; }

    public AgentHandle(AIAgent agent, ShellExecutor? shellExecutor, ChatOptions? chatOptions = null)
    {
        Agent = agent;
        _shellExecutor = shellExecutor;
        ChatOptions = chatOptions;
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
public class CharacterRunnerFactory : Singleton<CharacterRunnerFactory>, IInitialize
{
    /// <summary>
    /// shell 工具名(供预授权规则匹配)。装配时显式传给 <c>AsAIFunction</c>,
    /// 让这个常量成为唯一权威——否则它只是框架默认值的一份副本,框架改默认值就会静默失配。
    /// </summary>
    public const string ShellToolName = "Shell";

    /// <summary>定时任务调度后端(框架无对应能力,自建保留)</summary>
    public ISchedulerBackend Scheduler { get; private set; } = null!;

    public void OnInitialize()
    {
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
        // 能力配置来自角色本身:没有全局总闸,运行时只有这一份在说话(ADR 0003)
        AgentToolConfig config = character.Tools;

        // 角色自身的提示词(人格 + 用户卡 + 对话模板)。agent 档随后会在它之后接上工具纪律与
        // 工作区规矩(见 BuildAgentOptions)——顺序由我们说,不交给框架的分层
        ChatOptions chatOptions = character.Config.ExecutionSettings.ToChatOptions();
        chatOptions.Instructions = CharacterPromptBuilder.Build(character, profile.PromptArguments);

        List<AIContextProvider> contextProviders =
        [
            new MemoryContextProvider(hasKnowledgeTool:
                character.Kind.IsAgent() && config.EnableKnowledgeSearchTool),
        ];

        // 压缩阈值现读当前模型的上下文上限,与 LazyChatClient 取模型的口径完全一致:
        // agent 只在切工作区/权限档时重建,而模型随时可切,写死在构建期就会留下过期预算
        CompactionStrategy compaction = HistoryCompaction.Create(() =>
            (profile.SessionModelSource?.Invoke() ?? LlmManager.Instance.CurrentRunningModel)?.ContextLength ?? 0);

        // 只有 agent 档往下走装配。这里曾写作 == Roleplay:两档时代"非扮演即 agent"成立,
        // 四档之后工具人与用户卡会掉进 agent 分支,被装上文件/shell/技能与整套 harness
        if (!character.Kind.IsAgent())
        {
            return BuildHandle(client,
                BuildPromptOnlyOptions(character, history, contextProviders, chatOptions, compaction), null);
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
            extraTools.Add(shellExecutor.AsAIFunction(ShellToolName));
        }

        // 识图工具只在当前模型自己看不了图时才挂:视觉模型直接收图,ask_vision 是多余的绕路。
        // 该判定进装配快照,切换视觉/非视觉模型时下一次挂接自动重建
        bool modelSupportsVision =
            (profile.SessionModelSource?.Invoke() ?? LlmManager.Instance.CurrentRunningModel)?.IsVisionModel == true;
        bool mountVisionTool = config.EnableVisionTool && !modelSupportsVision;
        if (mountVisionTool)
        {
            extraTools.Add(VisionTool.Create(workingDirectory));
        }

        // 工作区规矩属任务上下文而非平台机制:排在我们这段的最尾(见 BuildAgentOptions)。
        // 注意它并非"整个系统提示的最尾"——框架的 provider 段(Todo / Agent Mode / File Based Memory)
        // 还在它之后,1.16 实测如此。内容变化仍经装配快照捕获。
        // 在此提前读出,是因为子代理要继承同一份
        string workspaceInstructions = WorkspaceInstructionsLoader.Load(profile.WorkspacePath);

        // 子代理:工具集与权限档都从主 agent 派生,全部能力都关掉时不挂载
        AITool? subAgentTool = config.EnableSubAgent
            ? TryCreateSubAgentTool(client, config, workingDirectory, mountVisionTool, profile,
                workspaceInstructions, compaction)
            : null;
        if (subAgentTool != null) extraTools.Add(subAgentTool);

        if (config.EnableKnowledgeSearchTool)
        {
            extraTools.Add(KnowledgeTool.Create(profile.SessionKnowledgeSource));
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

        // 目录名(角色名_id8)由挂接时的对账决定并写进会话状态,见 FileMemoryLayout;
        // store 只认这个父目录
        FileSystemAgentFileStore? fileMemoryStore = config.EnableFileMemory
            ? new FileSystemAgentFileStore(FileMemoryLayout.RootPath)
            : null;

        return BuildHandle(client, BuildAgentOptions(character, config, history, contextProviders, chatOptions,
            SkillCatalog.Instance.BuildSkillsSource(config.DisabledSkills), fileMemoryStore,
            profile.PermissionMode, profile.PreAuthorizedShellPatterns,
            profile.SessionShellApprovalSource,
            visionToolMounted: mountVisionTool, workingDirectory: workingDirectory,
            workspaceInstructions: workspaceInstructions, compaction: compaction), shellExecutor);
    }

    /// <summary>
    /// 子代理装配的输入。与 <see cref="AgentBuildProfile"/> 同一风格:把装配消费的东西列全,
    /// 使 <see cref="BuildSubAgentOptions"/> 成为不碰任何单例的纯函数。
    /// </summary>
    internal sealed record SubAgentAssemblyInput
    {
        /// <summary>
        /// 生效的能力配置。通用子代理即主代理那份;点名的子智能体是"它自己那份与主代理的交集"
        /// ——委派出去的不能比派活的能力更大
        /// </summary>
        public required AgentToolConfig Config { get; init; }

        /// <summary>
        /// 点名的子智能体的人格段(它自己的角色提示词);通用子代理为空串
        /// </summary>
        public string Persona { get; init; } = string.Empty;

        /// <summary>
        /// 子智能体名(框架侧 agent 名);空串则用通用的 "SubAgent"
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>文件工具的根目录</summary>
        public required string WorkingDirectory { get; init; }

        /// <summary>识图工具是否可挂(开关开且当前模型不自带视觉)</summary>
        public bool VisionToolAvailable { get; init; } = true;

        /// <summary>继承自主 agent 的权限档,决定可变更工具挂不挂</summary>
        public EAgentPermissionMode PermissionMode { get; init; } = EAgentPermissionMode.ReadOnly;

        /// <summary>工作区说明文件内容(与主 agent 同一份 AGENTS.md),拼在提示词最尾</summary>
        public string WorkspaceInstructions { get; init; } = string.Empty;

        /// <summary>
        /// shell 工具。有生命周期的资源,故由调用方创建并负责释放,不在纯函数里造。
        /// 只在完全自动档才该传进来。
        /// </summary>
        public AITool? ShellTool { get; init; }

        /// <summary>MCP 工具集(完全自动档才挂;它们可能改东西,较低档位下会卡在无人回应的审批上)</summary>
        public IReadOnlyList<AITool>? McpTools { get; init; }

        /// <summary>无人值守 shell 预授权模式(与主 agent 同源)</summary>
        public IReadOnlyList<string>? PreAuthorizedShellPatterns { get; init; }

        /// <summary>会话级 shell 放行模式来源(与主 agent 同源)</summary>
        public Func<IReadOnlyList<string>?>? SessionShellApprovalSource { get; init; }

        /// <summary>历史压缩策略(与主 agent 同一份);为 null 则不压缩</summary>
        public CompactionStrategy? Compaction { get; init; }
    }

    /// <summary>
    /// 创建子代理工具。每次调用重新装配:装配本身是纯内存组装代价可忽略,
    /// 而 shell 执行器是有生命周期的资源,必须一次调用一个、用完即弃。
    /// </summary>
    /// <param name="client">模型客户端(与主 agent 同一惰性客户端)</param>
    /// <param name="config">能力配置</param>
    /// <param name="workingDirectory">文件工具的根目录</param>
    /// <param name="visionToolAvailable">识图工具是否可挂</param>
    /// <param name="profile">主 agent 的构建配置(权限档与 shell 放行来源由此继承)</param>
    /// <param name="workspaceInstructions">工作区说明文件内容</param>
    /// <returns>工具;无任何能力可用时为 null</returns>
    private static AITool? TryCreateSubAgentTool(IChatClient client, AgentToolConfig config,
        string workingDirectory, bool visionToolAvailable, AgentBuildProfile profile,
        string workspaceInstructions, CompactionStrategy compaction)
    {
        bool fullAuto = profile.PermissionMode == EAgentPermissionMode.FullAuto;

        SubAgentAssemblyInput Probe(AITool? shellTool, IReadOnlyList<AITool>? mcpTools,
            AgentToolConfig effectiveConfig, string persona, string name) => new()
        {
            Compaction = compaction,
            Config = effectiveConfig,
            Persona = persona,
            Name = name,
            WorkingDirectory = workingDirectory,
            VisionToolAvailable = visionToolAvailable,
            PermissionMode = profile.PermissionMode,
            WorkspaceInstructions = workspaceInstructions,
            ShellTool = shellTool,
            McpTools = mcpTools,
            PreAuthorizedShellPatterns = profile.PreAuthorizedShellPatterns,
            SessionShellApprovalSource = profile.SessionShellApprovalSource,
        };

        // 先探一次:全部能力都关掉时不挂载(shell/MCP 不参与这个判定,它们只在完全自动档才有)
        if (BuildSubAgentOptions(Probe(null, null, config, string.Empty, string.Empty)) == null) return null;

        // 名单:角色挂的那些智能体。按档位过滤而非信任存档(旧存档里可能躺着工具人),并排除自己(递归)
        List<CharacterData> mounted = profile.Character.MountAgents
            .Select(id => CharacterManager.Instance.GetCharacterData(id))
            .Where(x => x.Kind.IsAgent() && x.CharacterId != profile.Character.CharacterId)
            .ToList();
        List<SubAgentChoice> roster = mounted
            .Select(x => new SubAgentChoice(SanitizeAgentName(x.CharacterName, x.CharacterId), x.Description))
            .ToList();

        return SubAgentTool.Create(agentName =>
        {
            // 点名的那一个:人格取它的 Template,能力取"它与父代理的交集"——
            // 挂一个开着 shell 的子智能体不该给关掉了 shell 的父代理开后门
            CharacterData? child = agentName == null
                ? null
                : mounted.FirstOrDefault(x =>
                    string.Equals(SanitizeAgentName(x.CharacterName, x.CharacterId), agentName,
                        StringComparison.OrdinalIgnoreCase));
            AgentToolConfig effective = child == null ? config : child.Tools.Intersect(config);
            string persona = child == null ? string.Empty : CharacterPromptBuilder.Build(child);
            string name = child == null
                ? string.Empty
                : SanitizeAgentName(child.CharacterName, child.CharacterId);

            LocalShellExecutor? shellExecutor = fullAuto && effective.EnableShellExecution
                ? new LocalShellExecutor(new LocalShellExecutorOptions { WorkingDirectory = workingDirectory })
                : null;
            AITool? shellTool = shellExecutor?.AsAIFunction(ShellToolName);
            IReadOnlyList<AITool>? mcpTools = fullAuto ? McpManager.Instance.GetCachedTools() : null;

            // 走同一个 BuildHandle:日志转发与工具错误详情两件事只有一处定义
            return BuildHandle(client,
                BuildSubAgentOptions(Probe(shellTool, mcpTools, effective, persona, name))!, shellExecutor);
        }, roster, profile.ActivitySink);
    }

    /// <summary>
    /// 子代理装配选项(纯函数,不碰单例)。不变量,均由测试钉住:
    /// 工具集<b>不含子代理工具自身</b>(无限递归);不含主代理特有的那批
    /// (技能/定时任务/记忆检索——子代理拿的是一份任务书,不需要再自己装载指令或排定时任务);
    /// <b>非完全自动档下必须只读</b>——子代理没有审批通道,给它一个必然要问用户的工具
    /// 等于给一把静默失效的工具(框架遇到 <c>ApprovalRequiredAIFunction</c> 不执行,
    /// 只产出一条无人回应的审批请求)。
    /// </summary>
    /// <param name="input">装配输入</param>
    /// <returns>框架选项;无任何能力启用时为 null</returns>
    internal static HarnessAgentOptions? BuildSubAgentOptions(SubAgentAssemblyInput input)
    {
        AgentToolConfig config = input.Config;
        // 完全自动档一律放行,子代理才可能真的写成东西;其余档位下写/shell 必然要问用户,故不挂
        bool canMutate = input.PermissionMode == EAgentPermissionMode.FullAuto;

        List<AITool> tools = new();
        if (config.EnableFileAccess)
        {
            tools.AddRange(new PermissiveFileAccessTools(input.WorkingDirectory)
                .Create(disableWriteTools: !canMutate));
        }

        if (config.EnableWebSearch)
        {
            tools.Add(WebSearchTool.Create());
            tools.Add(WebFetchTool.Create());
        }

        bool hasVision = config.EnableVisionTool && input.VisionToolAvailable;
        if (hasVision)
        {
            tools.Add(VisionTool.Create(input.WorkingDirectory));
        }

        if (canMutate)
        {
            if (input.ShellTool != null) tools.Add(input.ShellTool);
            if (input.McpTools != null) tools.AddRange(input.McpTools);
        }

        if (tools.Count == 0) return null;

        return new HarnessAgentOptions
        {
            Name = input.Name.Length > 0 ? input.Name : "SubAgent",
            Description = "Sub-agent: surveys workspace files and/or the web, inspects images, " +
                          "and returns a focused report.",
            // 与主 agent 同一口径:工作循环归"角色段"(这里就是 BuildSubAgentInstructions 生成的那份),
            // harness 段因此为空。框架默认那段的身份句会和下面的 "# Role" 抢身份,见 ADR 0004
            HarnessInstructions = string.Empty,
            // 无人值守兜底:到顶即停止循环并把已有进展作为响应返回(框架不抛异常)。
            // 子代理能改东西之后这条更承重
            MaximumIterationsPerRequest = SubAgentTool.MaxIterations,
            // 框架有状态能力全关,子代理是一次性的纯工具循环
            // (1.16 起框架文件工具只随 FileAccessStore 出现,不设即无,无需显式关闭)
            DisableWebSearch = true,
            DisableFileMemory = true,
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = true,
            // 压缩是唯一没关的框架能力:它只删不加,不往上下文里注入内容。
            // 16 轮工具循环最容易把上下文塞爆,子代理反而比谁都需要工具结果折叠(ADR 0006)
            DisableCompaction = input.Compaction == null,
            CompactionStrategy = input.Compaction,
            DisableOpenTelemetry = true,
            // 审批中间件照挂,规则与主 agent 同源——档位语义只有一处定义(ApprovalModeMapper)。
            // 非完全自动档下这里不会被用到:那些档位挂的全是免审批的只读工具
            ToolApprovalAgentOptions = new ToolApprovalAgentOptions
            {
                AutoApprovalRules = ApprovalModeMapper.BuildRules(input.PermissionMode,
                    input.PreAuthorizedShellPatterns, input.SessionShellApprovalSource),
            },
            ChatOptions = new ChatOptions
            {
                Instructions = BuildSubAgentInstructions(config, hasVision, canMutate,
                    input.WorkingDirectory, input.WorkspaceInstructions, input.Persona),
                Tools = tools,
            },
        };
    }

    /// <summary>
    /// 子代理的系统提示:身份 + 权限边界 + 报告体例(按实际装配的工具集裁剪)
    /// + 与主 agent 同一份工作区规矩。
    ///
    /// 工作区规矩必须给:子代理干的正是探查工作区的活,却会是全场唯一不知道工作区规矩的人——
    /// 本仓 AGENTS.md 头一条就是"有四层同名目录,用绝对路径别数相对层数",
    /// 拿着 Glob/Read 的子代理不知道这条就会直接踩进去。
    ///
    /// 全段由我们写死,不开放给调用方 AI:实测本地模型往自定义提示词里填的是与任务书重复的
    /// 泛泛套话,而固定段里指名的工具由我们保证真实存在(有不变量测试钉住),
    /// 调用方看不见子代理挂了哪些工具。
    /// </summary>
    /// <param name="config">能力配置</param>
    /// <param name="hasVision">识图工具是否已装配</param>
    /// <param name="canMutate">是否挂了可变更工具(完全自动档)</param>
    /// <param name="workingDirectory">文件与 shell 工具的根目录</param>
    /// <param name="workspaceInstructions">工作区说明文件内容</param>
    /// <returns>提示词</returns>
    private static string BuildSubAgentInstructions(AgentToolConfig config, bool hasVision, bool canMutate,
        string workingDirectory, string workspaceInstructions, string persona = "")
    {
        StringBuilder sb = new();
        // 点名的子智能体先说自己是谁(与主 agent 同一口径:人格在最前,见 ADR 0005),
        // 随后才是"你是被派活的子代理"这套边界与体例
        if (persona.Length > 0)
        {
            sb.AppendLine(persona.TrimEnd());
            sb.AppendLine();
        }

        sb.AppendLine("# Role");
        sb.AppendLine("You are a sub-agent of UiharuMind, delegated one task by the main agent. "
                      + "You work alone and report back.");
        sb.AppendLine();
        sb.AppendLine("# Method");
        if (config.EnableFileAccess)
        {
            sb.AppendLine($"- Explore workspace files with `{PermissiveFileAccessTools.GlobToolName}`, "
                          + $"`{PermissiveFileAccessTools.GrepToolName}` and "
                          + $"`{PermissiveFileAccessTools.ReadToolName}`. Pass explicit paths.");
        }

        if (config.EnableWebSearch)
        {
            sb.AppendLine($"- Research the web with `{WebSearchTool.ToolName}`, "
                          + $"then `{WebFetchTool.ToolName}` the promising results.");
        }

        if (hasVision)
        {
            sb.AppendLine($"- For image files, call `{VisionTool.ToolName}` with the file path.");
        }

        // 边界写清楚能省掉无效轮次:不然模型会反复去试没挂载的工具、吃失败、再换路
        sb.AppendLine(canMutate
            ? "- You may change things, but only what the task asks for. Nothing else."
            : "- You are read-only: you cannot write files and have no shell. Report what should change; "
              + "the main agent makes the edits.");
        sb.AppendLine("- You cannot ask for clarification and nobody will approve anything for you. "
                      + "Work with what the task gives you.");
        sb.AppendLine("- Return a focused report: conclusions first, then the evidence (paths, URLs, quotes).");
        sb.AppendLine();
        sb.Append(AgentToolPrompts.AgentWorkLoop);

        // 与主 agent 同一份措辞:子代理更需要这段,它连一句用户原话都看不到,
        // 没有任何线索能反推出根目录在哪
        if (workingDirectory.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("# Working directory");
            sb.Append(AgentToolPrompts.BuildWorkingDirectory(workingDirectory));
        }

        if (workspaceInstructions.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("# Workspace Instructions (from the project's AGENTS.md)");
            sb.Append(workspaceInstructions);
        }

        return sb.ToString();
    }

    // [MFA绕坑] 绕:框架默认向系统提示注入自身内容 因:无"纯透传"档,只能逐项 Disable 删除条件:框架提供 passthrough 模式
    /// <summary>
    /// 纯提示词档选项(扮演与工具人,纯函数,不碰单例)。不变量:框架侧一律关闭、HarnessInstructions 为空——
    /// 任何一项漏关都会向角色扮演的上下文里注入内容,该不变量由测试钉住。
    /// </summary>
    /// <param name="character">角色</param>
    /// <param name="history">历史提供器</param>
    /// <param name="contextProviders">上下文提供器</param>
    /// <param name="chatOptions">对话选项(含角色系统提示,工具应为空)</param>
    /// <returns>框架选项</returns>
    internal static HarnessAgentOptions BuildPromptOnlyOptions(CharacterData character,
        ChatHistoryProvider history, List<AIContextProvider> contextProviders, ChatOptions chatOptions,
        CompactionStrategy? compaction = null)
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
            // 「零注入」不变量的唯一例外:压缩只做排除与工具结果折叠,不向上下文添加任何内容,
            // 而扮演档的长对话同样会溢出——它没有工具调用,这里等价于纯截断(ADR 0006)
            DisableCompaction = compaction == null,
            CompactionStrategy = compaction,
            DisableToolAutoApproval = true,
            DisableOpenTelemetry = true,
            AIContextProviders = contextProviders,
            ChatOptions = chatOptions,
        };
    }

    /// <summary>
    /// agent 档选项(纯函数,不碰单例)。<b>整段系统提示由本方法按固定顺序拼</b>：
    /// 角色人格(含工作循环) → 用户卡 → 对话模板 → 工具纪律与工作目录 → 工作区规矩。
    ///
    /// 因此 <c>HarnessInstructions</c> 一律为空串：框架对它只做一件事——拼在角色段<b>之前</b>，
    /// 而人格该排在最前(见 ADR 0005)。框架自带的搜索/文件访问关闭,由自装配工具替代。
    /// </summary>
    /// <param name="character">角色</param>
    /// <param name="config">能力配置</param>
    /// <param name="history">历史提供器</param>
    /// <param name="contextProviders">上下文提供器</param>
    /// <param name="chatOptions">对话选项(含角色系统提示与已装配工具集,shell 工具已在其中)</param>
    /// <param name="skillsSource">技能来源</param>
    /// <param name="fileMemoryStore">文件记忆存储(父目录),禁用时为 null</param>
    /// <param name="permissionMode">权限档</param>
    /// <param name="preAuthorizedShellPatterns">无人值守 shell 预授权模式</param>
    /// <param name="sessionShellApprovalSource">会话级 shell 放行模式来源,可空</param>
    /// <param name="visionToolMounted">识图工具是否已装配(开关开且当前模型不自带视觉)</param>
    /// <param name="workingDirectory">文件与 shell 工具的根目录;空串则提示词里不写工作目录段</param>
    /// <param name="workspaceInstructions">工作区 AGENTS.md 内容;空串则不写该段</param>
    /// <returns>框架选项</returns>
    internal static HarnessAgentOptions BuildAgentOptions(CharacterData character, AgentToolConfig config,
        ChatHistoryProvider history, List<AIContextProvider> contextProviders, ChatOptions chatOptions,
        AgentSkillsSource skillsSource, FileSystemAgentFileStore? fileMemoryStore,
        EAgentPermissionMode permissionMode, IReadOnlyList<string>? preAuthorizedShellPatterns,
        Func<IReadOnlyList<string>?>? sessionShellApprovalSource = null,
        bool visionToolMounted = true, string workingDirectory = "", string workspaceInstructions = "",
        CompactionStrategy? compaction = null)
    {
        chatOptions.Instructions = ComposeAgentInstructions(chatOptions.Instructions, config,
            visionToolMounted, workingDirectory, workspaceInstructions);

        return new HarnessAgentOptions
        {
            Name = SanitizeAgentName(character.CharacterName, character.CharacterId),
            Description = character.Description,
            ChatHistoryProvider = history,
            // 空串是有意的:整段提示已由 ComposeAgentInstructions 按我们的顺序拼进 ChatOptions
            HarnessInstructions = string.Empty,
            DisableWebSearch = true,
            // 历史预算不再由我们裁剪,改由框架在环压缩按当前模型的上下文动态开窗(ADR 0006)
            DisableCompaction = compaction == null,
            CompactionStrategy = compaction,
            DisableOpenTelemetry = true,
            DisableTodoProvider = !config.EnableTodoList,
            DisableAgentModeProvider = !config.EnableAgentMode,
            FileMemoryStore = fileMemoryStore,
            // 1.16:框架文件工具只随 FileAccessStore 出现;shell 改为普通工具挂在 ChatOptions.Tools
            FileAccessStore = null,
            AgentSkillsSource = skillsSource,
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
        AIAgent agent = client.AsHarnessAgent(options, loggerFactory, services);

        // [MFA绕坑] 绕:工具异常只回给模型一句"Error: Function failed." 因:框架管道内部构造 FunctionInvokingChatClient,选项不外露 删除条件:HarnessAgentOptions 暴露该开关
        // 打开后回给模型的是"Error: Function failed. Exception: {e.Message}"(实测只有 Message,不含堆栈)。
        // 承重之处在于框架自带的 file_memory_* 工具:它们直接抛异常,而失败原因多半是
        // "要替换的原文没匹配上"这类模型自己能改的事——不告诉它,它只会原样重试。
        // 我们自建的工具都是自己 catch 后返回文字说明,不依赖这个开关。
        if (agent.GetService<FunctionInvokingChatClient>() is { } functionInvoker)
        {
            functionInvoker.IncludeDetailedErrors = true;
        }

        // 选项此刻已装配完毕,记在句柄上供旁路请求(写交接文档)复用同一份
        return new AgentHandle(agent, shellExecutor, options.ChatOptions);
    }

    private static string GetScratchDirectory()
    {
        string path = Path.Combine(SettingConfig.SaveAgentDataPath, "Scratch");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// 按固定顺序拼出 agent 档的整段系统提示：
    /// 角色段(人格 + 用户卡 + 对话模板) → 工具纪律与工作目录 → 工作区规矩。
    ///
    /// <b>人格在最前</b>：小模型要先知道自己是谁，再读一大段英文工具纪律。
    /// 这个顺序拿不到手过：框架只会把 <c>HarnessInstructions</c> 拼在角色段之前，
    /// 所以那一层弃用，整段自己拼(见 ADR 0005)。
    /// </summary>
    /// <param name="characterPrompt">角色段(CharacterPromptBuilder 的产物)</param>
    /// <param name="config">智能体的能力配置(角色自带)</param>
    /// <param name="visionToolMounted">识图工具是否已装配</param>
    /// <param name="workingDirectory">工作目录绝对路径;空串则不写该段</param>
    /// <param name="workspaceInstructions">工作区 AGENTS.md 内容;空串则不写该段</param>
    /// <returns>整段系统提示</returns>
    private static string ComposeAgentInstructions(string? characterPrompt, AgentToolConfig config,
        bool visionToolMounted, string workingDirectory, string workspaceInstructions)
    {
        StringBuilder sb = new();
        AppendSection(sb, characterPrompt);
        AppendSection(sb, BuildToolDisciplines(config, visionToolMounted, workingDirectory));
        if (workspaceInstructions.Length > 0)
        {
            AppendSection(sb,
                "# Workspace Instructions (from the project's AGENTS.md)\n" + workspaceInstructions);
        }

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string? section)
    {
        if (string.IsNullOrWhiteSpace(section)) return;
        if (sb.Length > 0) sb.Append("\n\n");
        sb.Append(section.TrimEnd());
    }

    /// <summary>
    /// 工具纪律段 = 按<b>实际装配的工具集</b>派生的使用纪律(外加工作目录这一事实段)。
    /// 纪律行面向弱模型:短句、祈使、指名工具;关掉的工具绝不出现(纯噪声)。
    ///
    /// <b>刻意不含工作循环</b>(先想再做/边做边说/失败换路/收尾总结)。那段现在是角色提示词的一节
    /// (<see cref="AgentToolPrompts.AgentWorkLoop"/>),理由见 ADR 0004:框架默认那段用户看不见,
    /// 还带一句"You are a helpful AI assistant"抢在角色人格之前。
    ///
    /// 本段由 <see cref="ComposeAgentInstructions"/> 接在角色段之后。
    /// </summary>
    /// <param name="config">智能体的能力配置(角色自带)</param>
    /// <param name="visionToolMounted">识图工具是否已装配</param>
    /// <returns>harness 层指令文本</returns>
    private static string BuildToolDisciplines(AgentToolConfig config, bool visionToolMounted,
        string workingDirectory)
    {
        StringBuilder sb = new();

        // 工作目录排在最前:后面每一段纪律都以"路径怎么写"为前提
        if (workingDirectory.Length > 0)
        {
            sb.AppendLine("## Working directory");
            sb.AppendLine(AgentToolPrompts.BuildWorkingDirectory(workingDirectory));
        }

        // 各段正文可在设置页覆盖(空 = 用 AgentToolPrompts 默认),段落标题固定由此处统一挂
        if (config.EnableFileAccess)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## File operations");
            sb.AppendLine(AgentToolPrompts.FileAccessDefault);
        }

        if (config.EnableVisionTool && visionToolMounted)
        {
            sb.AppendLine();
            sb.AppendLine("## Images");
            sb.AppendLine(AgentToolPrompts.VisionToolDefault);
        }

        // 文件记忆没有自己的纪律段:框架的 FileMemoryProvider 已经注入了一整段,见 AgentToolPrompts
        if (config.EnableKnowledgeSearchTool)
        {
            sb.AppendLine();
            sb.AppendLine("## Knowledge base");
            sb.AppendLine(AgentToolPrompts.KnowledgeSearchDefault);
        }

        // 辨析句只在两者都挂载时才有意义,故不属于任何一段的正文(那两段各自可被用户覆盖)
        if (config.EnableFileMemory && config.EnableKnowledgeSearchTool)
        {
            sb.AppendLine();
            sb.AppendLine(AgentToolPrompts.MemoryDisambiguation);
        }

        if (config.EnableSubAgent)
        {
            sb.AppendLine();
            sb.AppendLine("## Delegation");
            sb.AppendLine(AgentToolPrompts.SubAgentDefault);
        }

        return sb.ToString();
    }

    private static string SanitizeAgentName(string displayName, string fallback)
    {
        string name = new(displayName.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(name) ? fallback : name;
    }
}
