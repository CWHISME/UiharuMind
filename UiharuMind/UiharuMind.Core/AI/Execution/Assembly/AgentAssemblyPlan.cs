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
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.AI.Execution.Tools.Memory;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 一次装配所消费的<b>全部外部事实</b>，已经从单例与磁盘解析完毕。
///
/// 存在的理由是把「解析」与「装配」切成两段：<c>CreateAgent</c> 原本是 110 行的一个方法，
/// 里面混着 8 处单例读取、一次建目录、一次读盘，和十几步纯内存组装。
/// 切开之后 <see cref="AgentAssembler"/> 成为<b>不碰任何单例的纯函数</b>（因此可单测），
/// 而读外部世界只剩 <see cref="Resolve"/> 这一个入口。
///
/// 与 <see cref="AgentAssemblyFacts"/> 的分工按<b>取得代价</b>划线：facts 是能廉价取得
/// 又能按值比较的那一半，每次挂接都算一遍当作重建判据；只有它不等时才轮到这里，
/// 去建沙箱目录、造技能源、取 MCP 工具集这些贵的动作。
///
/// <b>活的钩子不在这里</b>——会话模型/知识库/shell 放行来源、过程上报口留在
/// <see cref="AgentBuildProfile"/> 上。它们每次请求现取，本就不该被固化成事实。
/// 压缩策略是个例外：它虽是闭包（阈值现读当前模型），但只在装配时挂一次，故随 plan 走。
/// </summary>
internal sealed class AgentAssemblyPlan
{
    /// <summary>调用方给的构建配置（含各路活的钩子）</summary>
    public required AgentBuildProfile Profile { get; init; }

    /// <summary>文件与 shell 工具的根目录：绑定的工作目录，或沙箱目录。非智能体档为空串</summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>工作区说明文件内容（AGENTS.md / CLAUDE.md）；无则空串</summary>
    public string WorkspaceInstructions { get; init; } = string.Empty;

    /// <summary>当前模型是否自带视觉。决定识图工具挂不挂——视觉模型直接收图，那工具是绕路</summary>
    public bool ModelSupportsVision { get; init; }

    /// <summary>挂载的子智能体（已按档位过滤并排除自己，见 <see cref="CharacterRunnerFactory.ResolveMountedAgents"/>）</summary>
    public IReadOnlyList<CharacterData> MountedAgents { get; init; } = [];

    /// <summary>
    /// MCP 侧的产物：工具集、server 自述与分组明细，取自装配时刻的那一份常驻缓存，
    /// 且已按角色的 <c>DisabledMcpServers</c> 过滤。
    /// 主代理与子代理用<b>同一份</b>——子代理原先在派活回调里现取，两者可能拿到不同的工具集，
    /// 而 MCP 侧变化本就由 <see cref="AgentAssemblyFacts.McpRevision"/> 触发重建
    /// </summary>
    public McpToolSet Mcp { get; init; } = McpToolSet.Empty;

    /// <summary>技能来源（已按角色的禁用清单过滤）；非智能体档为 null</summary>
    public AgentSkillsSource? SkillsSource { get; init; }

    /// <summary>文件记忆存储的父目录；能力关闭时为 null</summary>
    public FileSystemAgentFileStore? FileMemoryStore { get; init; }

    /// <summary>历史压缩策略；为 null 表示不压缩</summary>
    public CompactionStrategy? Compaction { get; init; }

    /// <summary>
    /// 本轮输入估算。压缩策略读它的固定开销、回写历史估算；装配末尾绑定到句柄。
    /// 与 <see cref="Compaction"/> 是同一次构造出来的一对，见 <see cref="TurnInputEstimate"/>
    /// </summary>
    public TurnInputEstimate InputEstimate { get; init; } = new();

    /// <summary>驱动整个装配的角色</summary>
    public CharacterData Character => Profile.Character;

    /// <summary>能力配置。没有全局总闸，运行时只有角色自带这一份在说话（ADR 0003）</summary>
    public AgentToolConfig Config => Profile.Character.Tools;

    /// <summary>识图工具是否该挂：开关开着，且当前模型自己看不了图</summary>
    public bool MountVisionTool => Config.EnableVisionTool && !ModelSupportsVision;

    /// <summary>
    /// 从构建配置解析出装配所需的全部事实。<b>这是唯一读单例与磁盘的地方</b>。
    /// </summary>
    /// <param name="profile">构建配置</param>
    /// <returns>装配计划</returns>
    public static AgentAssemblyPlan Resolve(AgentBuildProfile profile)
    {
        // 压缩阈值现读当前模型的上下文上限,与 LazyChatClient 取模型的口径完全一致:
        // agent 只在切工作区/权限档时重建,而模型随时可切,写死在构建期就会留下过期预算。
        // 固定开销此刻还算不出来(它含系统提示,而提示是装配现场拼的),故经盒子迟到绑定
        TurnInputEstimate estimate = new();
        CompactionStrategy compaction =
            HistoryCompaction.Create(() => CurrentModel(profile)?.ContextLength ?? 0, estimate);

        // 非智能体档不装配任何工具:下面这些解析既用不上,又带副作用(建沙箱目录、读盘)。
        // 这里曾写作 == Roleplay:两档时代"非扮演即 agent"成立,
        // 四档之后工具人与用户卡会掉进 agent 分支,被装上文件/shell/技能与整套 harness
        if (!profile.Character.Kind.IsAgent())
        {
            return new AgentAssemblyPlan { Profile = profile, Compaction = compaction, InputEstimate = estimate };
        }

        AgentToolConfig config = profile.Character.Tools;
        return new AgentAssemblyPlan
        {
            Profile = profile,
            Compaction = compaction,
            InputEstimate = estimate,
            WorkingDirectory = profile.WorkspacePath ?? GetScratchDirectory(),
            // 提前读出:子代理要继承同一份
            WorkspaceInstructions = WorkspaceInstructionsLoader.Load(profile.WorkspacePath),
            ModelSupportsVision = CurrentModel(profile)?.IsVisionModel == true,
            MountedAgents = config.EnableSubAgent
                ? CharacterRunnerFactory.ResolveMountedAgents(profile.Character)
                : [],
            Mcp = McpManager.Instance.Resolve(config.DisabledMcpServers),
            SkillsSource = SkillCatalog.Instance.BuildSkillsSource(config.DisabledSkills),
            // 目录名(角色名_id8)由挂接时的对账决定并写进会话状态,见 FileMemoryLayout;
            // store 只认这个父目录
            FileMemoryStore = config.EnableFileMemory
                ? new FileSystemAgentFileStore(FileMemoryLayout.RootPath)
                : null,
        };
    }

    /// 会话绑定模型优先,回落全局当前模型——与 LazyChatClient 同一解析次序
    private static ModelRunningData? CurrentModel(AgentBuildProfile profile) =>
        profile.SessionModelSource?.Invoke() ?? LlmManager.Instance.CurrentRunningModel;

    private static string GetScratchDirectory()
    {
        string path = Path.Combine(SettingConfig.SaveAgentDataPath, "Scratch");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }
}
