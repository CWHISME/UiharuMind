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
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Harness;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.Memory;
using UiharuMind.Core.AI.Execution.Tools.Scheduler;
using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 装配本体：把一份解析好的 <see cref="AgentAssemblyPlan"/> 组装成可运行的 agent。
///
/// <b>不碰任何单例、不读磁盘</b>——外部世界在 <see cref="AgentAssemblyPlan.Resolve"/> 里就问完了。
/// 因此这一整条路径可以单测，而它原先混在工厂的 110 行 <c>CreateAgent</c> 里，一个测试都没有。
///
/// 角色扮演与 agent 走同一个引擎，差异全部落在 <see cref="HarnessAgentOptions"/> 上：
/// 角色扮演档把框架的每一项能力都关掉、工具集为空、HarnessInstructions 为空串，
/// 使框架不向系统提示里添加任何内容——等价于一次纯聊天调用，外加白拿的运行中插话能力。
///
/// 装配是纯同步的内存组装：MCP 工具取 plan 里那份常驻缓存的快照，绝不等待网络。
/// 重建时机由 <see cref="AgentAssemblyFacts"/> 差异决定。
/// </summary>
internal static class AgentAssembler
{
    /// <summary>
    /// 按计划装配一个 agent
    /// </summary>
    /// <param name="plan">已解析完外部事实的装配计划</param>
    /// <returns>agent 句柄</returns>
    public static AgentHandle Assemble(AgentAssemblyPlan plan)
    {
        AgentBuildProfile profile = plan.Profile;
        CharacterData character = plan.Character;

        // 子会话走子代理那条装配：它的能力是「自己的 ∩ 派活者的」并受只读裁剪，
        // 与主代理那条路产出的形状不同。判据取会话上持久化的身份而非调用方参数——
        // 重开一个子会话续跑时没人再传参数，走错路就是把不变量违掉（见 ADR 0021）
        if (profile.SubAgent != null) return SubAgentAssembly.BuildFromPlan(plan);

        IChatClient client = new LazyChatClient(profile.SessionModelSource);
        // 历史落到自有会话文件,框架 blob 里只剩 todos/mode/审批与一个会话标识指针
        SessionChatHistoryProvider history = new();

        // 角色自身的提示词(人格 + 用户卡 + 对话模板)。agent 档随后会在它之后接上工具纪律与
        // 工作区规矩(见 AgentOptionsFactory.BuildAgentOptions)——顺序由我们说,不交给框架的分层
        ChatOptions chatOptions = character.Config.ExecutionSettings.ToChatOptions();
        chatOptions.Instructions = CharacterPromptBuilder.Build(character, profile.PromptArguments);

        List<AIContextProvider> contextProviders = BuildContextProviders(plan);

        if (!character.Kind.IsAgent())
        {
            return BuildHandle(client,
                AgentOptionsFactory.BuildPromptOnlyOptions(character, history, contextProviders, chatOptions,
                    plan.Compaction), null, inputEstimate: plan.InputEstimate);
        }

        // ShellEnvironment 只在受管 Python 环境就绪时非空,内容是一次 venv 激活(见 AgentAssemblyPlan)
        LocalShellExecutor? shellExecutor = plan.Config.EnableShellExecution
            ? ShellExecutorFactory.Create(plan.WorkingDirectory, plan.ShellEnvironment)
            : null;

        List<AgentToolEntry> toolEntries = BuildTools(plan, client, shellExecutor);
        chatOptions.Tools = toolEntries.Select(x => x.Tool).ToList();

        // 构造后即可读,不必等 InitializeAsync(实测)。读不到就不写那一句——
        // 宁可不说,也不能告诉模型一个错的 shell
        string shellBinary = shellExecutor?.ResolvedShellBinary ?? string.Empty;

        HarnessAgentOptions options = AgentOptionsFactory.BuildAgentOptions(plan, history, contextProviders,
            chatOptions, shellBinary, out IReadOnlyList<AgentPromptSegment> promptSegments);
        return BuildHandle(client, options, shellExecutor, plan.Mcp, toolEntries, promptSegments,
            plan.InputEstimate);
    }

    /// <summary>
    /// 装配上下文 provider 链。<b>顺序是契约，不是排版</b>：框架把这一串追加在它自己那批之后
    /// （<c>HarnessAgent.BuildContextProviders</c>），并按列表顺序串行执行、每个都收到累积后的
    /// <c>AIContext</c>。因此 <see cref="InjectedContextRewriter"/> 必须是<b>最后一项</b>——
    /// 它靠"排在后面"才看得见（并改得动）前面 provider 的产出。有不变量测试钉住。
    /// </summary>
    /// <param name="plan">装配计划</param>
    /// <returns>provider 链，按执行顺序</returns>
    internal static List<AIContextProvider> BuildContextProviders(AgentAssemblyPlan plan)
    {
        CharacterData character = plan.Character;
        List<AIContextProvider> providers =
        [
            new MemoryContextProvider(hasKnowledgeTool:
                character.Kind.IsAgent() && plan.Config.EnableKnowledgeSearchTool),
        ];

        // 只在文件记忆开着时挂:它目前只治那一个 provider 的产出,关着时挂上去是一次空遍历。
        // 非智能体档整个禁用了框架文件记忆(见 ADR 0003),那里也没有可改写的东西
        if (character.Kind.IsAgent() && plan.Config.EnableFileMemory)
            providers.Add(new InjectedContextRewriter());

        return providers;
    }

    /// <summary>
    /// 装配 agent 档的工具集。挂哪些由角色的能力配置决定，与档位无关的判定（识图是否多余、
    /// 子代理是否有能力可用）已经在 plan 里算好。
    ///
    /// 每个工具连同<b>它属于哪一档能力</b>一并登记（见 <see cref="AgentToolEntry"/>）：
    /// 角色编辑页要按开关显示占用，而事后靠名字表反推归属，工具一改名就静默错位。
    /// </summary>
    /// <param name="plan">装配计划</param>
    /// <param name="client">模型客户端（子代理与主代理共用同一惰性客户端）</param>
    /// <param name="shellExecutor">shell 执行器；未启用为 null</param>
    /// <returns>工具集，每项带能力归属</returns>
    private static List<AgentToolEntry> BuildTools(AgentAssemblyPlan plan, IChatClient client,
        LocalShellExecutor? shellExecutor)
    {
        AgentToolConfig config = plan.Config;
        List<AgentToolEntry> tools = new();

        void Add(EAgentCapability capability, AITool tool) => tools.Add(new AgentToolEntry(capability, tool));

        if (shellExecutor != null)
        {
            // 1.16:shell 作为普通工具挂载,默认名即 run_shell、默认自包审批,预授权规则按名匹配不变
            Add(EAgentCapability.Shell, shellExecutor.AsAIFunction(CharacterRunnerFactory.ShellToolName));
        }

        // 识图工具只在当前模型自己看不了图时才挂:视觉模型直接收图,ViewImage 是多余的绕路。
        // 该判定进装配快照,切换视觉/非视觉模型时下一次挂接自动重建
        if (plan.MountVisionTool)
        {
            Add(EAgentCapability.VisionTool, VisionTool.Create(plan.WorkingDirectory));
        }

        // 子代理:工具集与权限档都从主代理派生,全部能力都关掉时不挂载
        // 通用子代理:可改文件(FullAuto 下),用主代理模型
        if (config.EnableSubAgent && SubAgentAssembly.TryCreateTool(plan, client, SubAgentProfile.General) is { } subAgentTool)
        {
            Add(EAgentCapability.SubAgent, subAgentTool);
        }
        // 探索子代理:始终只读,可用轻量模型(在设置页 agent 专用页签里选)
        if (config.EnableSubAgent && SubAgentAssembly.TryCreateTool(plan, client, SubAgentProfile.Explorer) is { } explorerTool)
        {
            Add(EAgentCapability.SubAgent, explorerTool);
        }

        // 续跑/追问:只在真的挂上了派活工具时才挂——没派过活就没有子会话可续。
        // 判据取<b>装配结果</b>而不是配置意图,与纪律段同一口径
        if (tools.Any(x => x.Capability == EAgentCapability.SubAgent)
            && SubAgentAssembly.TryCreateContinueTool(plan) is { } continueTool)
        {
            Add(EAgentCapability.SubAgent, continueTool);
        }

        if (config.EnableKnowledgeSearchTool)
        {
            Add(EAgentCapability.KnowledgeSearch, KnowledgeTool.Create(plan.Profile.SessionKnowledgeSource));
        }

        if (config.EnableScheduledTasks)
        {
            Add(EAgentCapability.ScheduledTasks, SchedulerTools.CreateScheduledTaskTool(plan.Profile.WorkspacePath));
        }

        foreach (AITool mcpTool in plan.Mcp.Tools) Add(EAgentCapability.Mcp, mcpTool);

        if (config.EnableFileAccess)
        {
            foreach (AITool tool in new PermissiveFileAccessTools(plan.WorkingDirectory).Create())
            {
                Add(EAgentCapability.FileAccess, tool);
            }
        }

        if (config.EnableWebSearch)
        {
            Add(EAgentCapability.WebSearch, WebSearchTool.Create());
            Add(EAgentCapability.WebSearch, WebFetchTool.Create());
        }

        return tools;
    }

    /// <summary>
    /// 把装配好的选项变成一个可运行的 agent 句柄
    /// </summary>
    /// <param name="client">模型客户端</param>
    /// <param name="options">框架选项</param>
    /// <param name="shellExecutor">shell 执行器（有生命周期，交给句柄释放）</param>
    /// <param name="mcp">本次装配的 MCP 产物（右栏据此展示归属）；子代理与纯提示词档不传</param>
    /// <param name="toolEntries">带能力归属的工具集；子代理与纯提示词档不传</param>
    /// <param name="promptSegments">系统提示的分段清单；子代理与纯提示词档不传</param>
    /// <param name="inputEstimate">与本次压缩策略配对的输入估算；子代理不传</param>
    /// <returns>agent 句柄</returns>
    internal static AgentHandle BuildHandle(IChatClient client, HarnessAgentOptions options,
        ShellExecutor? shellExecutor, McpToolSet? mcp = null,
        IReadOnlyList<AgentToolEntry>? toolEntries = null,
        IReadOnlyList<AgentPromptSegment>? promptSegments = null,
        TurnInputEstimate? inputEstimate = null)
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
            // 同一回复内的多个工具调用并行执行(框架默认串行)。全局开、不按工具分档,
            // 依赖顺序的调用靠提示词让模型拆成多轮;子代理由此才能真的并发。见 ADR 0022 的修订段
            functionInvoker.AllowConcurrentInvocation = true;
        }

        // 选项此刻已装配完毕,记在句柄上供旁路请求(写交接文档)复用同一份
        return new AgentHandle(agent, shellExecutor, options.ChatOptions, mcp, toolEntries, promptSegments,
            inputEstimate);
    }
}
