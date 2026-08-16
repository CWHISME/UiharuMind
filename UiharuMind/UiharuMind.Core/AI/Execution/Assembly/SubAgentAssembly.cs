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
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.WebTools;
using UiharuMind.Core.AI.Execution.Mcp;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 子代理那一摊：输入契约、工具创建、框架选项与提示词。
///
/// 独立成文件是因为它是<b>一个完整的小装配</b>——自己的能力交集规则、自己的权限边界、
/// 自己的提示词体例，与主 agent 的装配只共享工作目录与工作区规矩这两样输入。
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
    /// <param name="plan">主 agent 的装配计划（工作目录、工作区规矩、权限档与名单由此继承）</param>
    /// <param name="client">模型客户端(与主 agent 同一惰性客户端)</param>
    /// <returns>工具;无任何能力可用时为 null</returns>
    public static AITool? TryCreateTool(AgentAssemblyPlan plan, IChatClient client)
    {
        AgentBuildProfile profile = plan.Profile;
        AgentToolConfig config = plan.Config;
        string workingDirectory = plan.WorkingDirectory;
        bool fullAuto = profile.PermissionMode == EAgentPermissionMode.FullAuto;

        SubAgentAssemblyInput Probe(AITool? shellTool, IReadOnlyList<AITool>? mcpTools,
            AgentToolConfig effectiveConfig, string persona, string name) => new()
        {
            Compaction = plan.Compaction,
            Config = effectiveConfig,
            Persona = persona,
            Name = name,
            WorkingDirectory = workingDirectory,
            VisionToolAvailable = plan.MountVisionTool,
            PermissionMode = profile.PermissionMode,
            WorkspaceInstructions = plan.WorkspaceInstructions,
            ShellTool = shellTool,
            McpTools = mcpTools,
            PreAuthorizedShellPatterns = profile.PreAuthorizedShellPatterns,
            SessionShellApprovalSource = profile.SessionShellApprovalSource,
        };

        // 先探一次:全部能力都关掉时不挂载(shell/MCP 不参与这个判定,它们只在完全自动档才有)
        if (BuildSubAgentOptions(Probe(null, null, config, string.Empty, string.Empty)) == null) return null;

        IReadOnlyList<CharacterData> mounted = plan.MountedAgents;
        List<SubAgentChoice> roster = mounted
            .Select(x => new SubAgentChoice(AgentOptionsFactory.SanitizeAgentName(x.CharacterName, x.CharacterId), x.Description))
            .ToList();

        return SubAgentTool.Create(agentName =>
        {
            // 点名的那一个:人格取它的 Template,能力取"它与父代理的交集"——
            // 挂一个开着 shell 的子智能体不该给关掉了 shell 的父代理开后门
            CharacterData? child = agentName == null
                ? null
                : mounted.FirstOrDefault(x =>
                    string.Equals(AgentOptionsFactory.SanitizeAgentName(x.CharacterName, x.CharacterId), agentName,
                        StringComparison.OrdinalIgnoreCase));
            AgentToolConfig effective = child == null ? config : child.Tools.Intersect(config);
            string persona = child == null ? string.Empty : CharacterPromptBuilder.Build(child);
            string name = child == null
                ? string.Empty
                : AgentOptionsFactory.SanitizeAgentName(child.CharacterName, child.CharacterId);

            LocalShellExecutor? shellExecutor = fullAuto && effective.EnableShellExecution
                ? new LocalShellExecutor(new LocalShellExecutorOptions { WorkingDirectory = workingDirectory })
                : null;
            AITool? shellTool = shellExecutor?.AsAIFunction(CharacterRunnerFactory.ShellToolName);
            // 与主 agent 同一份(装配时刻那一份)。这里曾经在派活回调里现取,
            // 于是主子可能拿到不同的工具集;而工具集变化本就由 McpRevision 触发整体重建,
            // 现取除了制造不一致,还让这一支永远碰不到单例、测不了
            IReadOnlyList<AITool>? mcpTools = fullAuto ? plan.McpTools : null;

            // 走同一个 BuildHandle:日志转发与工具错误详情两件事只有一处定义
            return AgentAssembler.BuildHandle(client,
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

        // 框架有状态能力全关,子代理是一次性的纯工具循环
        // (1.16 起框架文件工具只随 FileAccessStore 出现,不设即无,无需显式关闭)。
        // 压缩是唯一没关的:它只删不加,而 16 轮工具循环最容易把上下文塞爆,
        // 子代理反而比谁都需要工具结果折叠(ADR 0006)
        HarnessAgentOptions options =
            AgentOptionsFactory.CreateSubAgentBaseOptions(input.Compaction);
        options.Name = input.Name.Length > 0 ? input.Name : "SubAgent";
        options.Description = "Sub-agent: surveys workspace files and/or the web, inspects images, " +
                              "and returns a focused report.";
        // 无人值守兜底:到顶即停止循环并把已有进展作为响应返回(框架不抛异常)。
        // 子代理能改东西之后这条更承重
        options.MaximumIterationsPerRequest = SubAgentTool.MaxIterations;
        // 审批中间件照挂,规则与主 agent 同源——档位语义只有一处定义(ApprovalModeMapper)。
        // 非完全自动档下这里不会被用到:那些档位挂的全是免审批的只读工具
        options.ToolApprovalAgentOptions = new ToolApprovalAgentOptions
        {
            AutoApprovalRules = ApprovalModeMapper.BuildRules(input.PermissionMode,
                input.PreAuthorizedShellPatterns, input.SessionShellApprovalSource),
        };
        options.ChatOptions = new ChatOptions
        {
            Instructions = BuildSubAgentInstructions(config, hasVision, canMutate,
                input.WorkingDirectory, input.WorkspaceInstructions, input.Persona),
            Tools = tools,
        };
        return options;
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
        // 没有任何线索能反推出根目录在哪。段落正文经 AgentInstructionsComposer 共用,
        // 这里只是没有 # Tools 那层外壳,故标题用一级
        if (workingDirectory.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(AgentInstructionsComposer.WorkingDirectorySection(workingDirectory, "#"));
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
