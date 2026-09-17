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
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution.Prompts;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.WebTools;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 子代理那一摊：输入契约、工具创建、框架选项与提示词。
///
/// 独立成文件是因为它是<b>一个完整的小装配</b>——自己的能力交集规则、自己的权限边界、
/// 自己的提示词体例，与主代理的装配只共享工作目录与工作区规矩这两样输入。
/// 混在工厂里时，这 200 行是「工厂到底有多大」里最难辨认的一块。
/// </summary>
internal static class SubAgentAssembly
{
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

        /// <summary>继承自主代理的权限档,决定可变更工具挂不挂</summary>
        public EAgentPermissionMode PermissionMode { get; init; } = EAgentPermissionMode.ReadOnly;

        /// <summary>工作区说明文件内容(与主代理同一份 AGENTS.md),拼在提示词最尾</summary>
        public string WorkspaceInstructions { get; init; } = string.Empty;

        /// <summary>
        /// shell 工具。有生命周期的资源,故由调用方创建并负责释放,不在纯函数里造。
        /// 只在完全自动档才该传进来。
        /// </summary>
        public AITool? ShellTool { get; init; }

        /// <summary>实际解析出来的 shell 可执行路径;没挂 shell 则为 null</summary>
        public string? ShellBinary { get; init; }

        /// <summary>MCP 工具集(完全自动档才挂;它们可能改东西,较低档位下会卡在无人回应的审批上)</summary>
        public IReadOnlyList<AITool>? McpTools { get; init; }

        /// <summary>MCP server 自述(与主代理同一份),随工具一起给</summary>
        public string McpInstructions { get; init; } = string.Empty;

        /// <summary>无人值守 shell 预授权模式(与主代理同源)</summary>
        public IReadOnlyList<string>? PreAuthorizedShellPatterns { get; init; }

        /// <summary>会话级 shell 放行模式来源(与主代理同源)</summary>
        public Func<IReadOnlyList<string>?>? SessionShellApprovalSource { get; init; }

        /// <summary>历史压缩策略(与主代理同一份);为 null 则不压缩</summary>
        public CompactionStrategy? Compaction { get; init; }

        /// <summary>子代理策略(类型、模型源、提示词侧重点);未传时用通用子代理</summary>
        public SubAgentProfile SubAgentProfile { get; init; } = SubAgentProfile.General;

        /// <summary>派活时给的一句话身份/职业(可选);空串表示未设定。注入子代理的「# 角色」段</summary>
        public string Role { get; init; } = string.Empty;

        /// <summary>受管 Python 环境的产出目录(空串=环境未就绪)。子代理继承派活者会话的产出目录,产出直接落那里</summary>
        public string PythonOutputDirectory { get; init; } = string.Empty;

        /// <summary>产出房间目录名(派活者会话那一间)。审批把这一间视为界内,与 Python 环境是否就绪无关</summary>
        public string OutputFolderName { get; init; } = string.Empty;
    }

    /// <summary>
    /// 按一份<b>子会话</b>的装配计划直接造出子代理句柄。
    ///
    /// 与 <see cref="TryCreateTool"/> 的分工：那边是「主代理要一把委派工具」，
    /// 派活时在闭包里现装；这边是「一个子会话要跑自己的轮次」，走的是
    /// <c>AgentAssembler.Assemble</c> 的正规路径。两条路必须产出同一形状的 agent——
    /// 否则重开一个子会话续跑时，装配出来的能力会与它当初被派出去时不一致。
    ///
    /// 子会话的角色：点名的那一种就是子智能体本人（人格取它的，能力取交集）；
    /// 匿名的那一种角色沿用派活者（于是能力天然等于派活者那一份），但<b>人格必须为空</b>——
    /// 通用子代理不是派活者的分身。
    /// </summary>
    /// <param name="plan">子会话的装配计划（<c>Profile.SubAgent</c> 必须非空）</param>
    /// <returns>agent 句柄</returns>
    public static AgentHandle BuildFromPlan(AgentAssemblyPlan plan)
    {
        SubSessionAssembly assembled = BuildSubSessionAssembly(plan);
        // 模型走会话覆写(派活时已把解析结果钉在子会话上,见 SubAgentTool.ResolveSubAgentModelName)——
        // 与主代理同一条解析链,于是界面显示的模型与实际问话的那个<b>由构造保证一致</b>
        return AgentAssembler.BuildHandle(new LazyChatClient(plan.Profile.SessionModelSource),
            assembled.Options, assembled.Shell);
    }

    /// <summary>子会话装配的产物：框架选项，以及要随句柄一同释放的 shell 执行器</summary>
    /// <param name="Options">框架选项</param>
    /// <param name="Shell">shell 执行器；未挂时为 null</param>
    internal readonly record struct SubSessionAssembly(HarnessAgentOptions Options, LocalShellExecutor? Shell);

    /// <summary>
    /// 把子会话的装配计划变成框架选项。<b>与造 agent 分开</b>是为了能不起模型地单测——
    /// 「历史持久化接没接上」这类缺陷是静默的（会话照跑、盘上什么都没有、界面一片空白），
    /// 只能靠测试在装配这一层拦住。
    /// </summary>
    /// <param name="plan">子会话的装配计划</param>
    /// <returns>选项与 shell 执行器</returns>
    internal static SubSessionAssembly BuildSubSessionAssembly(AgentAssemblyPlan plan)
    {
        AgentBuildProfile profile = plan.Profile;
        SubAgentIdentity identity = profile.SubAgent
                                    ?? throw new InvalidOperationException("BuildFromPlan 只接受子会话的装配计划");
        SubAgentProfile subProfile = identity.Type == ESubAgentType.Explorer
            ? SubAgentProfile.Explorer
            : SubAgentProfile.General;

        EAgentPermissionMode effectivePermission = subProfile.ForceReadOnly
            ? EAgentPermissionMode.ReadOnly
            : profile.PermissionMode;
        // 挂不挂 shell/MCP 与档位无关了,只看是不是探索档——能不能真的执行由审批规则把关
        bool canMutate = !subProfile.ForceReadOnly;

        bool named = identity.AgentName.Length > 0;
        // 点名的那一个:能力取「自己的 ∩ 派活者的」——挂一个开着 shell 的子智能体，
        // 不该给关掉了 shell 的派活者开后门。
        //
        // 匿名的那一个:<b>直接取派活者那一份，不走交集</b>。它的角色卡(内置 SubAgent)
        // 只是身份载体，那张卡上的 Tools 不参与计算——否则 AgentToolConfig 将来新增一个
        // 默认关闭的能力，匿名子代理就会悄悄少一样东西，而没有任何地方会报错。
        //
        // 派活者已被删除时退回只用自己的那一份(只会更小，见 SubAgentParentConfig)
        AgentToolConfig effectiveConfig = plan.SubAgentParentConfig is { } parentConfig
            ? (named ? plan.Config.Intersect(parentConfig) : parentConfig)
            : plan.Config;
        string persona = named ? CharacterPromptBuilder.Build(plan.Character, profile.PromptArguments) : string.Empty;

        LocalShellExecutor? shellExecutor = canMutate && effectiveConfig.EnableShellExecution
            ? ShellExecutorFactory.Create(plan.WorkingDirectory, plan.ShellEnvironment)
            : null;
        AITool? shellTool = shellExecutor?.AsAIFunction(CharacterRunnerFactory.ShellToolName);
        IReadOnlyList<AITool>? mcpTools = canMutate ? plan.Mcp.Tools : null;

        SubAgentAssemblyInput input = new()
        {
            Compaction = plan.Compaction,
            Config = effectiveConfig,
            Persona = persona,
            Name = identity.AgentName,
            WorkingDirectory = plan.WorkingDirectory,
            VisionToolAvailable = plan.MountVisionTool,
            PermissionMode = effectivePermission,
            WorkspaceInstructions = plan.WorkspaceInstructions,
            ShellTool = shellTool,
            ShellBinary = shellExecutor?.ResolvedShellBinary,
            McpTools = mcpTools,
            McpInstructions = mcpTools == null ? string.Empty : plan.Mcp.Instructions,
            PreAuthorizedShellPatterns = profile.PreAuthorizedShellPatterns,
            SessionShellApprovalSource = profile.SessionShellApprovalSource,
            SubAgentProfile = subProfile,
            Role = identity.Role,
            PythonOutputDirectory = plan.PythonOutputDirectory,
            OutputFolderName = profile.OutputFolderName,
        };

        HarnessAgentOptions? options = BuildSubAgentOptions(input);
        if (options == null)
        {
            // 一个能力都没有:仍要给出一个可运行的 agent(否则这个子会话打不开),
            // 但它只剩纯对话——能力被裁到零本身就是派活者那边的配置结果
            options = AgentOptionsFactory.CreateSubAgentBaseOptions(plan.Compaction);
            options.Name = identity.AgentName.Length > 0 ? identity.AgentName : "SubAgent";
            options.ChatOptions = new ChatOptions { Instructions = persona };
        }

        // 历史落到子会话自己的文件里。<b>没有这一句子会话就等于没跑过</b>——
        // 框架不写、ChatSession.History 恒空、窗口一片空白、HistoryAppended 永不触发。
        // 主代理那条路在 AgentOptionsFactory.BuildAgentOptions 里设同一个东西;
        // 从前子代理是一次性的纯工具循环,不需要它,于是 CreateSubAgentBaseOptions 里没有
        options.ChatHistoryProvider = new SessionChatHistoryProvider();

        return new SubSessionAssembly(options, shellExecutor);
    }

    /// <summary>
    /// 创建子代理工具。每次调用重新装配:装配本身是纯内存组装代价可忽略,
    /// 而 shell 执行器是有生命周期的资源,必须一次调用一个、用完即弃。
    /// </summary>
    /// <param name="plan">主代理的装配计划（工作目录、工作区规矩、权限档与名单由此继承）</param>
    /// <param name="client">模型客户端(与主代理同一惰性客户端)</param>
    /// <param name="subProfile">子代理策略;未传时用通用子代理(兼容现有调用方)</param>
    /// <returns>工具;无任何能力可用时为 null</returns>
    public static AITool? TryCreateTool(AgentAssemblyPlan plan, IChatClient client,
        SubAgentProfile? subProfile = null)
    {
        subProfile ??= SubAgentProfile.General;
        AgentBuildProfile profile = plan.Profile;
        AgentToolConfig config = plan.Config;
        string workingDirectory = plan.WorkingDirectory;
        // 探索型始终只读,覆盖主代理的权限档;通用型继承主代理的权限档
        EAgentPermissionMode effectivePermission = subProfile.ForceReadOnly
            ? EAgentPermissionMode.ReadOnly
            : profile.PermissionMode;
        bool fullAuto = effectivePermission == EAgentPermissionMode.FullAuto;

        SubAgentAssemblyInput Probe(AITool? shellTool, string? shellBinary, IReadOnlyList<AITool>? mcpTools,
            AgentToolConfig effectiveConfig, string persona, string name) => new()
        {
            Compaction = plan.Compaction,
            Config = effectiveConfig,
            Persona = persona,
            Name = name,
            WorkingDirectory = workingDirectory,
            VisionToolAvailable = plan.MountVisionTool,
            PermissionMode = effectivePermission,
            WorkspaceInstructions = plan.WorkspaceInstructions,
            ShellTool = shellTool,
            ShellBinary = shellBinary,
            McpTools = mcpTools,
            // 自述绑在工具上:没给工具就不该给用法,那只是白占上下文
            McpInstructions = mcpTools == null ? string.Empty : plan.Mcp.Instructions,
            PreAuthorizedShellPatterns = profile.PreAuthorizedShellPatterns,
            SessionShellApprovalSource = profile.SessionShellApprovalSource,
            SubAgentProfile = subProfile,
        };

        // 先探一次:全部能力都关掉时不挂载(shell/MCP 不参与这个判定,它们只在完全自动档才有)
        if (BuildSubAgentOptions(Probe(null, null, null, config, string.Empty, string.Empty)) == null) return null;

        IReadOnlyList<CharacterData> mounted = plan.MountedAgents;
        List<SubAgentChoice> roster = mounted
            .Select(x => new SubAgentChoice(
                AgentOptionsFactory.SanitizeAgentName(x.CharacterName, x.CharacterId),
                x.Description, x.CharacterId))
            .ToList();

        return SubAgentTool.Create(BuildLaunchContext(plan, subProfile, roster));
    }

    /// <summary>
    /// 创建续跑/追问工具。只在<b>至少挂上了一档派活工具</b>时才挂——
    /// 没有派过活就没有子会话可续，白占一份工具定义（固定开销每轮重发）。
    /// </summary>
    /// <param name="plan">派活者的装配计划</param>
    /// <returns>工具；不该挂时为 null</returns>
    public static AITool? TryCreateContinueTool(AgentAssemblyPlan plan)
    {
        List<SubAgentChoice> roster = plan.MountedAgents
            .Select(x => new SubAgentChoice(
                AgentOptionsFactory.SanitizeAgentName(x.CharacterName, x.CharacterId),
                x.Description, x.CharacterId))
            .ToList();
        return SubAgentTool.CreateContinueTool(BuildLaunchContext(plan, SubAgentProfile.General, roster));
    }

    /// <summary>
    /// 组一份派活上下文。子会话的字段几乎全部从派活者继承，因此这里没有任何决策，
    /// 只有搬运——真正的装配发生在子会话自己挂接执行者的那一刻（<see cref="BuildFromPlan"/>）。
    /// </summary>
    /// <param name="plan">派活者的装配计划</param>
    /// <param name="subProfile">子代理档</param>
    /// <param name="roster">可点名的子智能体名单</param>
    /// <returns>派活上下文</returns>
    internal static SubAgentTool.LaunchContext BuildLaunchContext(AgentAssemblyPlan plan,
        SubAgentProfile subProfile, IReadOnlyList<SubAgentChoice> roster)
    {
        AgentBuildProfile profile = plan.Profile;
        return new SubAgentTool.LaunchContext
        {
            ParentSessionId = profile.SessionId,
            ParentOutputFolderName = profile.OutputFolderName,
            WorkspacePath = profile.WorkspacePath,
            PermissionModeIndex = (int)profile.PermissionMode,
            PreAuthorizedShellPatterns = profile.PreAuthorizedShellPatterns,
            Profile = subProfile,
            Roster = roster,
            IsAttendedSource = profile.IsAttendedSource,
            SubSessionStarted = profile.SubSessionStarted,
        };
    }

    /// <summary>
    /// 子代理装配选项(纯函数,不碰单例)。不变量,均由测试钉住:
    /// 工具集<b>不含子代理工具自身</b>(无限递归);不含主代理特有的那批
    /// (技能/定时任务/记忆检索——子代理拿的是一份任务书,不需要再自己装载指令或排定时任务);
    /// <b>探索档恒定只读</b>(产品决定:调研不该顺手改东西)。其余档位挂什么由能力配置定、
    /// 能不能动手由 <see cref="ApprovalModeMapper"/> 定——与主代理同一口径,
    /// 因为子代理现在跑自己的 <c>TurnDriver</c>,审批请求冒到派活者这一轮的回应口(ADR 0021)。
    /// </summary>
    /// <param name="input">装配输入</param>
    /// <returns>框架选项;无任何能力启用时为 null</returns>
    internal static HarnessAgentOptions? BuildSubAgentOptions(SubAgentAssemblyInput input)
    {
        AgentToolConfig config = input.Config;
        // 子代理现在有审批通道了(它跑自己的 TurnDriver,请求冒到派活者这一轮的回应口),
        // 于是「非完全自动档必须只读」那条硬裁剪解除——挂什么由能力配置定,
        // 能不能动手由 ApprovalModeMapper 定,与主代理完全同一口径(见 ADR 0021)。
        //
        // 唯一仍然恒定只读的是<b>探索档</b>:那是产品决定而不是技术限制
        // (调研就该只读,免得一次"看一眼"顺手改了东西)。
        bool canMutate = !input.SubAgentProfile.ForceReadOnly;

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

        bool hasVision = canMutate && config.EnableVisionTool && input.VisionToolAvailable;
        // 与 hasVision 同一口径:纪律段里指名的工具必须真的在这份工具集里(有不变量测试钉住),
        // 所以判据取"装配结果"而不是"配置意图"——shell 只在完全自动档随 ShellTool 挂上
        bool hasShell = canMutate && input.ShellTool != null;
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

        // 框架有状态能力全关,子代理是一次性的纯工具循环
        // (1.16 起框架文件工具只随 FileAccessStore 出现,不设即无,无需显式关闭)。
        // 压缩是唯一没关的:它只删不加,而 16 轮工具循环最容易把上下文塞爆,
        // 子代理反而比谁都需要工具结果折叠(ADR 0006)
        HarnessAgentOptions options =
            AgentOptionsFactory.CreateSubAgentBaseOptions(input.Compaction);
        options.Name = input.Name.Length > 0 ? input.Name : "SubAgent";
        // Description 会被框架注入系统提示,而身份段已在 BuildSubAgentInstructions 里完整写出——
        // 再给一份英文描述就是重复,还会把整段中文提示词带出英文。留空。
        options.Description = string.Empty;
        // 无人值守兜底:到顶即停止循环并把已有进展作为响应返回(框架不抛异常)。
        // 子代理能改东西之后这条更承重
        options.MaximumIterationsPerRequest = SubAgentTool.MaxIterations;
        // 审批中间件照挂,规则与主代理同源——档位语义只有一处定义(ApprovalModeMapper)。
        // 非完全自动档下这里不会被用到:那些档位挂的全是免审批的只读工具
        options.ToolApprovalAgentOptions = new ToolApprovalAgentOptions
        {
            AutoApprovalRules = ApprovalModeMapper.BuildRules(input.PermissionMode,
                input.WorkingDirectory, input.PreAuthorizedShellPatterns, input.SessionShellApprovalSource,
                // 子会话沿用派活者的房间(见 AgentBuildProfile),豁免同一间
                AgentOutputLayout.GetRoomAbsolutePath(input.OutputFolderName)),
        };
        options.ChatOptions = new ChatOptions
        {
            Instructions = BuildSubAgentInstructions(config, hasVision, hasShell,
                input.ShellBinary ?? string.Empty, canMutate, input.PythonOutputDirectory,
                input.WorkingDirectory,
                AgentOutputLayout.GetRoomAbsolutePath(input.OutputFolderName),
                input.WorkspaceInstructions, input.McpInstructions,
                input.Persona, input.Role, input.SubAgentProfile),
            Tools = tools,
        };
        return options;
    }

    /// <summary>
    /// 子代理的系统提示:身份 + 权限边界 + 报告体例(按实际装配的工具集裁剪)
    /// + 与主代理同一份工作区规矩。
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
    /// <param name="hasShell">命令行工具是否已装配</param>
    /// <param name="shellBinary">实际解析出来的 shell 可执行路径;空串则不写那一句</param>
    /// <param name="canMutate">是否挂了可变更工具(完全自动档)</param>
    /// <param name="workingDirectory">文件与 shell 工具的根目录</param>
    /// <param name="outputRoomDirectory">草稿目录(派活者会话的房间)绝对路径；空串则不写该段</param>
    /// <param name="workspaceInstructions">工作区说明文件内容</param>
    /// <param name="mcpInstructions">MCP server 自述（与主代理同一份）</param>
    /// <returns>提示词</returns>
    private static string BuildSubAgentInstructions(AgentToolConfig config, bool hasVision, bool hasShell,
        string shellBinary, bool canMutate, string pythonOutputDirectory,
        string workingDirectory, string outputRoomDirectory,
        string workspaceInstructions, string mcpInstructions,
        string persona = "", string role = "", SubAgentProfile? subProfile = null)
    {
        subProfile ??= SubAgentProfile.General;
        StringBuilder sb = new();
        // 点名的子智能体先说自己是谁(与主代理同一口径:人格在最前,见 ADR 0005),
        // 随后才是"你是被派活的子代理"这套边界与体例
        if (persona.Length > 0)
        {
            sb.AppendLine(persona.TrimEnd());
            sb.AppendLine();
        }

        sb.AppendLine(AgentPromptHeadings.SubAgentRole);
        sb.AppendLine(SubAgentPrompts.Role);
        sb.AppendLine(subProfile.RoleHint);
        if (role.Length > 0)
        {
            sb.AppendLine($"你这次的身份：{role}。");
        }
        // 护栏句:本段整段中文,而子代理连一句用户原话都看不到,更容易被提示词的语言带跑
        sb.AppendLine(AgentToolPrompts.LanguageNeutrality);
        sb.AppendLine(AgentToolPrompts.ConcurrentCalls);
        sb.AppendLine();
        sb.AppendLine(AgentPromptHeadings.SubAgentMethod);
        if (config.EnableFileAccess)
        {
            sb.AppendLine(SubAgentPrompts.MethodFileAccess(FileToolNames.Glob, FileToolNames.Grep, FileToolNames.Read));
        }

        if (config.EnableWebSearch)
        {
            sb.AppendLine(SubAgentPrompts.MethodWebSearch(WebSearchTool.ToolName, WebFetchTool.ToolName));
        }

        if (hasVision)
        {
            sb.AppendLine(SubAgentPrompts.MethodVision(VisionTool.ToolName));
        }

        // 边界写清楚能省掉无效轮次:不然模型会反复去试没挂载的工具、吃失败、再换路
        sb.AppendLine(canMutate
            ? SubAgentPrompts.BoundaryCanMutate
            : SubAgentPrompts.BoundaryReadOnly);
        // 完全自动档的子代理拿的是同一个 Shell,不该是全场唯一不知道怎么用它的人
        if (hasShell)
        {
            sb.AppendLine(AgentToolPrompts.BuildShell(config.EnableFileAccess, shellBinary));
        }

        // 同一把 Shell 也跑 python。产出目录沿用派活者会话的(派活时固化在子会话上),
        // 子代理的产出与主代理落在同一目录,报告里给绝对路径主代理可直接引用
        if (hasShell && pythonOutputDirectory.Length > 0)
        {
            sb.AppendLine(AgentToolPrompts.BuildPython(config.EnableFileAccess));
        }

        // 审批通道存在(ADR 0021):子代理现在跑自己的轮次,需要审批的操作会问到用户那里。
        // 不再写"不会有人替你批准"——那半句已不成立,留着会让子代理误以为权限问题必须绕开
        sb.AppendLine(SubAgentPrompts.MethodAskForMissing);
        sb.AppendLine(SubAgentPrompts.MethodDiscussion);
        sb.AppendLine(SubAgentPrompts.MethodFocusedReply);
        sb.AppendLine(SubAgentPrompts.MethodEndWithText);
        sb.AppendLine();
        sb.Append(AgentToolPrompts.AgentWorkLoop);

        // 与主代理同一份措辞:子代理更需要这段,它连一句用户原话都看不到,
        // 没有任何线索能反推出根目录在哪。段落正文经 AgentInstructionsComposer 共用,
        // 这里只是没有 # 工具 那层外壳,故标题用一级
        if (workingDirectory.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(AgentInstructionsComposer.WorkingDirectorySection(workingDirectory, "#"));
        }

        // 草稿目录与主代理同一段正文。只在真有地方可写时出现:探索档无写工具又无 shell,
        // 说了也只是指一个写不进去的目录;`Write`/`Edit` 那句另由写工具是否在场决定
        if (outputRoomDirectory.Length > 0 && (hasShell || (config.EnableFileAccess && canMutate)))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine(AgentPromptHeadings.OutputRoom("#"));
            sb.Append(AgentToolPrompts.BuildOutputRoom(outputRoomDirectory));
        }

        // 挂了 MCP 工具就得给对应的自述:只给签名不给用法,子代理照样不会用
        if (canMutate && mcpInstructions.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(AgentInstructionsComposer.McpSection(mcpInstructions));
        }

        if (workspaceInstructions.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(AgentInstructionsComposer.WorkspaceSection(workspaceInstructions));
        }

        return sb.ToString();
    }
}
