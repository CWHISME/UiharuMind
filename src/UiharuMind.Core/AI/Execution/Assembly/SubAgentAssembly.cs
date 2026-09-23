/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution.Prompts;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools.WebTools;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 子代理那一摊：输入契约、工具创建、框架选项与提示词。
///
/// 独立成文件是因为它是<b>一个完整的小装配</b>——自己的能力交集规则、自己的权限边界、
/// 自己的身份与协作口径。混在工厂里时，这 200 行是「工厂到底有多大」里最难辨认的一块。
///
/// <b>工具纪律不在这里</b>：那一张段落清单两档共用（<see cref="ToolDisciplineSections"/>）。
/// 从前这边手写一套、主代理那边手写另一套，差异不是设计而是漂移——
/// 子代理拿着 Edit/Write 却从没收到修改纪律，工作目录在一边排最前、在这边排最后。
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

        /// <summary>工作区说明文件内容(与主代理同一份 AGENTS.md);装配时只取有无——有就给指针</summary>
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

        /// <summary>
        /// 采样参数(取子会话自己那张角色卡)。点名的即子智能体本人那张;匿名的即它实际沿用的那张。
        /// 为 null 时不发采样参数——测试手造输入沿用旧行为,生产路径(<see cref="BuildFromPlan"/>)
        /// 恒定赋值。从前这里根本没这一项:两处 <c>new ChatOptions</c> 裸建,
        /// 卡上的 temperature 改了也到不了子代理。
        /// </summary>
        public ChatPromptExecutionSettings? Sampling { get; init; }

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
            Sampling = plan.Character.Config.ExecutionSettings,
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
            ChatOptions fallback = input.Sampling?.ToChatOptions() ?? new ChatOptions();
            fallback.Instructions = persona;
            options.ChatOptions = fallback;
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

    // 从前这里有一把独立的续跑工具（ContinueAgent）。ADR 0044 之后它退役了：
    // 新开与续跑是同一个动作——给某个人发消息，区别只在这个人是刚认识还是已经聊过，
    // 由 SendMessage 的 to 参数自己分流（人名 → 新开；子会话标识 → 续上）。
    // 少一把工具，就少一份每轮重发的固定开销。

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

        // 探索档与「恒定只读」同源(产品决定而非技术限制):它干的是工作区内的初级调研,
        // 不挂联网——Glob/Grep/Read 三个只读文件工具就是它的全部
        bool hasWeb = canMutate && config.EnableWebSearch;
        if (hasWeb)
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
        ChatOptions subOptions = input.Sampling?.ToChatOptions() ?? new ChatOptions();
        subOptions.Instructions = BuildSubAgentInstructions(config, hasWeb, hasVision, hasShell,
            input.ShellBinary ?? string.Empty, canMutate, input.PythonOutputDirectory,
            input.WorkingDirectory,
            AgentOutputLayout.GetRoomAbsolutePath(input.OutputFolderName),
            input.WorkspaceInstructions, input.McpInstructions,
            input.Persona, input.Role);
        subOptions.Tools = tools;
        options.ChatOptions = subOptions;
        return options;
    }

    /// <summary>
    /// 子代理的系统提示。段序：身份 → 工具纪律 → 工作循环 → 协作口径 → MCP 自述 → 工作区规矩。
    /// <b>与主代理同构</b>——中间那段工具纪律逐字取自同一张清单，只有开关不同。
    ///
    /// 「# 工作循环」<b>点名的子智能体要跳过</b>：新建智能体时它已被预填进角色卡
    /// （ADR 0004），再追加一份就是同一份提示词里出现两次。有不变量测试钉住。
    ///
    /// 工作区规矩必须给:子代理干的正是探查工作区的活,却会是全场唯一不知道工作区规矩的人——
    /// 本仓 AGENTS.md 头一条就是"有四层同名目录,用绝对路径别数相对层数",
    /// 拿着 Glob/Read 的子代理不知道这条就会直接踩进去。
    /// 呈现与主代理同一口径:只给指针、正文由模型 Read 自读;若探索档弱模型实测出现"没读就编"
    /// 的 case,把装配点切回 AgentInstructionsComposer.WorkspaceSection 的截断版即可。
    ///
    /// 全段由我们写死,不开放给调用方 AI:实测本地模型往自定义提示词里填的是与任务书重复的
    /// 泛泛套话,而固定段里指名的工具由我们保证真实存在(有不变量测试钉住),
    /// 调用方看不见子代理挂了哪些工具。
    /// </summary>
    /// <param name="config">能力配置</param>
    /// <param name="hasWeb">联网工具是否已装配(探索档恒为 false,见装配处)</param>
    /// <param name="hasVision">识图工具是否已装配</param>
    /// <param name="hasShell">命令行工具是否已装配</param>
    /// <param name="shellBinary">实际解析出来的 shell 可执行路径;空串则不写那一句</param>
    /// <param name="canMutate">是否挂了可变更工具(完全自动档)</param>
    /// <param name="workingDirectory">文件与 shell 工具的根目录</param>
    /// <param name="outputRoomDirectory">草稿目录(派活者会话的房间)绝对路径；空串则不写该段</param>
    /// <param name="workspaceInstructions">工作区说明文件内容(只取有无,有则给指针,不拼正文)</param>
    /// <param name="mcpInstructions">MCP server 自述（与主代理同一份）</param>
    /// <returns>提示词</returns>
    private static string BuildSubAgentInstructions(AgentToolConfig config, bool hasWeb, bool hasVision, bool hasShell,
        string shellBinary, bool canMutate, string pythonOutputDirectory,
        string workingDirectory, string outputRoomDirectory,
        string workspaceInstructions, string mcpInstructions,
        string persona = "", string role = "")
    {
        bool named = persona.Length > 0;

        // 身份段。点名的子智能体先说自己是谁(与主代理同一口径:人格在最前,见 ADR 0005),
        // 且<b>不再跟一句"你是 UiharuMind 的一个代理"</b>——那是跟人格抢身份
        // 身份段内部用单换行:全是短句,空行撑开既费 token 又让它看着像五段独立的话
        List<string> identity = [];
        if (!named) identity.Add(SubAgentPrompts.Role);
        if (role.Length > 0)
        {
            identity.Add(SubAgentPrompts.RoleAssignment(role));
            // 角色卡的长度与具体度都碾压那一句 role,不表态的话 role 会被压过去
            if (named) identity.Add(SubAgentPrompts.RoleOverPersona);
        }

        // 边界句置空(见 SubAgentPrompts 注释):能力由工具集决定,规则由任务书指明。非空才入列。
        string? boundary = canMutate ? SubAgentPrompts.BoundaryCanMutate : SubAgentPrompts.BoundaryReadOnly;
        if (boundary.Length > 0) identity.Add(boundary);
        // 并行调用那句护栏归 # 工具 段(与主代理同一处),不放这里:
        // 它讲的是工具调用语义,没有工具时毫无意义,挂在身份段等于"你是谁"后面
        // 突然接一条并发规则

        PromptSectionList list = new();
        list.Raw(named, persona);
        list.Section(true, AgentPromptHeadings.SubAgentRole, string.Join("\n", identity));

        // 工具纪律与主代理共用同一张清单(段序、出现条件都在那一处定义)
        list.Raw(true, ToolDisciplineSections.Build(new ToolDisciplineSections.ToolDisciplineFacts
        {
            FileRead = config.EnableFileAccess,
            // 探索档拿的是 disableWriteTools 裁过的那份:有 Glob/Grep/Read,没有 Edit/Write
            FileWrite = config.EnableFileAccess && canMutate,
            Shell = hasShell,
            Python = hasShell && pythonOutputDirectory.Length > 0,
            // 与 hasVision 同口径:段只在实际挂了工具时出现——探索档恒不挂(见装配处)
            WebAccess = hasWeb,
            Vision = hasVision,
            // 子代理不挂知识库工具,也不能再派子代理(防无限递归)
            KnowledgeBase = false,
            Delegation = false,
            WorkingDirectory = workingDirectory,
            OutputRoom = outputRoomDirectory,
            // 记忆是主代理专有的:子代理拿的是一份任务书,不需要自己装载跨会话笔记(ADR 0028)
            Memory = string.Empty,
            ShellBinary = shellBinary,
            ForSubAgent = true,
        }));

        // 工作循环:点名的子智能体<b>跳过</b>——新建智能体时这一段已被预填进它的角色卡
        // (HomePageData.NewCharacterAsync),再追加一份就是同一份提示词里出现两次
        list.Raw(!named, AgentToolPrompts.AgentWorkLoop);
        list.Section(true, AgentPromptHeadings.SubAgentCollaboration, SubAgentPrompts.Collaboration);

        // 挂了 MCP 工具就得给对应的自述:只给签名不给用法,子代理照样不会用
        list.Raw(canMutate && mcpInstructions.Length > 0,
            AgentInstructionsComposer.McpSection(mcpInstructions));
        list.Raw(workspaceInstructions.Length > 0,
            AgentInstructionsComposer.WorkspacePointerSection());

        return list.ToString();
    }
}
