/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Core.AI.Execution.Tools.Desktop;
using UiharuMind.Core.AI.Execution.Tools.Scheduler;
using UiharuMind.Core.AI.Execution.Tools.WebTools;
using UiharuMind.Core.AI.Memory;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 装配工具要用到的现成资源，由各自的装配路径备好（主代理取自装配计划，子代理取自派活上下文）。
/// 挂不挂只由 <see cref="AgentToolsetBuilder"/> 按能力与场合决定；只在某些场合用得上的给成工厂，用不上就不造
/// </summary>
internal sealed record AgentToolResources
{
    /// <summary>文件、识图与各扩展共用的路径口径</summary>
    public required AgentPathResolver Paths { get; init; }

    /// <summary>会话 id：浏览器一个会话一个标签页，群发言认它</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>会话的产出房间（浏览器快照超长、截图落在这里）；空串退回临时目录</summary>
    public string OutputRoomDirectory { get; init; } = string.Empty;

    /// <summary>当前模型自带视觉：Read 直接附图；看不了图时识图另挂 AnalyzeImage</summary>
    public bool ModelSupportsVision { get; init; }

    /// <summary>配了生图模型（没配时生图扩展照样列出，调用时说明去哪儿配）</summary>
    public bool ImageModelsConfigured { get; init; }

    /// <summary>生图进度交给界面；没有界面在看为 null</summary>
    public Action<string, ImageProgress>? ImageProgress { get; init; }

    /// <summary>MCP：可用的 server 名单与查、调它们的桥接（每个 server 是一个扩展）</summary>
    public McpToolSet Mcp { get; init; } = McpToolSet.Empty;

    /// <summary>造 shell 工具，参数是同一会话挂没挂后台任务；没开 shell 为 null</summary>
    public Func<bool, AITool>? Shell { get; init; }

    /// <summary>造后台任务工具（有 shell 且场合允许才造）</summary>
    public Func<AITool>? BackgroundTasks { get; init; }

    /// <summary>造委派的两把工具；能力全关时返回 null</summary>
    public Func<IReadOnlyList<AITool>?>? Delegation { get; init; }

    /// <summary>造脑工具（ADR 0082，同步只读整理助手）；能力全关时返回 null</summary>
    public Func<AITool?>? Brain { get; init; }

    /// <summary>浏览器宿主</summary>
    public IBrowserHost? Browser { get; init; }

    /// <summary>桌面宿主（ADR 0084）</summary>
    public DesktopHost? Desktop { get; init; }

    /// <summary>朗读的出口（界面念给用户听）；没有会话时为 null</summary>
    public Func<SpeechRequest, Task<bool>>? SpeechSink { get; init; }

    /// <summary>配音不点名时用谁的声音；没有时音频扩展只给转写</summary>
    public CharacterData? Character { get; init; }

    /// <summary>会话挂载的知识库</summary>
    public Func<MemoryData?>? KnowledgeSource { get; init; }

    /// <summary>定时任务落在哪个工作区</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>化身：这次离席是否无限期（结束离席工具据此措辞）</summary>
    public Func<bool>? AwayIsInfinite { get; init; }

    /// <summary>技能总闸关着时顶上的 load_skill；总闸开着为 null</summary>
    public Func<AITool>? LoadSkill { get; init; }
}

/// <summary>
/// 工具集装配的唯一入口：主代理、群成员、化身、定时运行、子代理都从这里拿工具。
/// 挂哪几档 = 生效能力（角色卡 ∩ 派活者）∩ 场合（<see cref="AgentToolScopes"/>）；每档工具怎么造只写在这一处。
///
/// 每个工具连同<b>它属于哪一档能力</b>一并登记（见 <see cref="AgentToolEntry"/>）：
/// 角色编辑页按开关显示占用，事后靠名字表反推归属的话，工具一改名就静默错位。
/// 顺序就是请求里 tools 的顺序，它在缓存前缀里，别随手调。
/// </summary>
internal static class AgentToolsetBuilder
{
    /// <summary>
    /// 装配一份工具集
    /// </summary>
    /// <param name="config">生效能力（子代理已与派活者取过交集）</param>
    /// <param name="scope">场合</param>
    /// <param name="resources">现成资源</param>
    /// <returns>工具集，每项带能力归属</returns>
    public static List<AgentToolEntry> Build(AgentToolConfig config, EAgentToolScope scope,
        AgentToolResources resources)
    {
        AgentToolScopeRule rule = AgentToolScopes.Of(scope);
        List<AgentToolEntry> tools = new();
        void Add(EAgentCapability capability, AITool tool) => tools.Add(new AgentToolEntry(capability, tool));
        bool Mounts(EAgentCapability capability, bool enabled) => enabled && rule.Allows(capability);
        AgentPathResolver paths = resources.Paths;

        if (Mounts(EAgentCapability.Shell, config.EnableShellExecution) && resources.Shell is { } shell)
        {
            bool backgroundTasks = rule.BackgroundTasks && resources.BackgroundTasks != null;
            Add(EAgentCapability.Shell, shell(backgroundTasks));
            if (backgroundTasks) Add(EAgentCapability.Shell, resources.BackgroundTasks!());
        }

        // 识图只在当前模型自己看不了图时挂：视觉模型经 Read 直接看图，AnalyzeImage 是多余的绕路
        bool mountVision = Mounts(EAgentCapability.VisionTool, config.EnableVisionTool) && !resources.ModelSupportsVision;
        if (mountVision) Add(EAgentCapability.VisionTool, VisionTool.Create(paths));
        bool mountFiles = Mounts(EAgentCapability.FileAccess, config.EnableFileAccess);
        // 视觉模型读到图就附上；否则指向 AnalyzeImage（它真的挂着才指）；都不成立就如实说看不了
        EReadImageMode imageMode = config.EnableVisionTool && resources.ModelSupportsVision ? EReadImageMode.Attach
            : mountVision ? EReadImageMode.PointToAnalyzeImage
            : EReadImageMode.None;

        bool mountSummary = false;
        if (Mounts(EAgentCapability.SubAgent, config.EnableSubAgent))
        {
            if (resources.Delegation?.Invoke() is { } delegation)
            {
                foreach (AITool tool in delegation) Add(EAgentCapability.SubAgent, tool);
            }

            // 脑（ADR 0082）：与委派同源，同步只读的小模型整理助手。挂在同一能力档下——
            // 关委派就关脑，子会话场合（SubAgent/Brain scope）自然不递归
            if (resources.Brain?.Invoke() is { } brain)
            {
                Add(EAgentCapability.SubAgent, brain);
                mountSummary = true;
            }
        }

        if (rule.Allows(EAgentCapability.GroupPost)) Add(EAgentCapability.GroupPost, GroupPostTool.Create(resources.SessionId));
        if (rule.Allows(EAgentCapability.GroupAway) && resources.AwayIsInfinite is { } awayIsInfinite)
        {
            Add(EAgentCapability.GroupAway, EndAwayTool.Create(awayIsInfinite));
        }

        // 子代理 → 主代理：收件人固定是派活者，只在子代理场合挂（场合表说话），脑不挂（ADR 0082）
        if (rule.Allows(EAgentCapability.ParentMessage))
        {
            Add(EAgentCapability.ParentMessage, ParentMessageTool.Create(resources.SessionId));
        }

        if (Mounts(EAgentCapability.Speak, config.EnableSpeak) && resources.SpeechSink is { } speechSink)
        {
            Add(EAgentCapability.Speak, SpeakTool.Create(speechSink));
        }

        if (Mounts(EAgentCapability.KnowledgeSearch, config.EnableKnowledgeSearchTool))
        {
            Add(EAgentCapability.KnowledgeSearch, KnowledgeTool.Create(resources.KnowledgeSource));
        }

        if (Mounts(EAgentCapability.ScheduledTasks, config.EnableScheduledTasks))
        {
            Add(EAgentCapability.ScheduledTasks, SchedulerTools.CreateScheduledTaskTool(resources.WorkspacePath));
        }

        if (rule.Allows(EAgentCapability.Extensions))
        {
            List<BuiltinExtension> builtins = Builtins(config, resources, imageMode, mountFiles);
            // 目标模式的隐藏扩展：只挂主代理场合（目标模式是主会话的事，子代理/群成员/化身不跑它）。
            // 常驻可调用（模型经注入消息得知）、不进广告名单，因此不占 DisabledExtensions
            if (scope == EAgentToolScope.Main) builtins.Add(BuiltinExtensions.CreateGoalMode());
            if (builtins.Count > 0 || resources.Mcp.Bridge != null)
            {
                foreach (AITool tool in ExtensionMetaTools.Create(new ExtensionHub(builtins, resources.Mcp.Bridge)))
                {
                    Add(EAgentCapability.Extensions, tool);
                }
            }
        }

        if (mountFiles)
        {
            foreach (AITool tool in new PermissiveFileAccessTools(paths, imageMode: imageMode, offerSummary: mountSummary)
                         .Create(disableWriteTools: rule.ReadOnlyFiles))
            {
                Add(EAgentCapability.FileAccess, tool);
            }
        }

        if (Mounts(EAgentCapability.WebSearch, config.EnableWebSearch))
        {
            Add(EAgentCapability.WebSearch, WebSearchTool.Create());
            Add(EAgentCapability.WebSearch, WebFetchTool.Create());
        }

        // 技能模型可见性总闸关闭时,框架的 load_skill 不挂,由自建同名工具顶上:
        // 模型仍能按名加载广告列表内的技能(被动技能),主动技能正文引用它们才不落空
        if (rule.Allows(EAgentCapability.Skills) && resources.LoadSkill is { } loadSkill)
        {
            Add(EAgentCapability.Skills, loadSkill());
        }

        return tools;
    }

    /// <summary>
    /// 这次装配列出的内置扩展：角色没禁的都给（ADR 0079），缺前提的照样列出、调用时说明原因。
    /// </summary>
    private static List<BuiltinExtension> Builtins(AgentToolConfig config, AgentToolResources resources,
        EReadImageMode imageMode, bool mountFiles)
    {
        List<BuiltinExtension> builtins = new();
        HashSet<string> listed = ListedBuiltins(config).ToHashSet();
        AgentPathResolver paths = resources.Paths;

        // 一个会话一个标签页；快照超长、截图落进会话自己的草稿目录，模型按路径续读。
        // 截图得看得了：视觉模型要有 Read 才看得见，非视觉模型要有 AnalyzeImage
        if (listed.Contains(BuiltinExtensions.Browser) && resources.Browser == null)
        {
            // 名单照写（与系统提示同一判据），调用时如实说用不了，免得名单与帮助各说各的
            builtins.Add(BuiltinExtensions.Unavailable(BuiltinExtensions.Browser, "No browser is available here."));
        }
        else if (listed.Contains(BuiltinExtensions.Browser))
        {
            string spillDirectory = Path.Combine(
                resources.OutputRoomDirectory.Length > 0 ? resources.OutputRoomDirectory : Path.GetTempPath(), "browser");
            EReadImageMode screenshots = imageMode == EReadImageMode.Attach && !mountFiles ? EReadImageMode.None : imageMode;
            builtins.Add(BuiltinExtensions.CreateBrowser(resources.Browser!, resources.SessionId, spillDirectory, screenshots,
                paths));
        }

        // 桌面：系统不支持、没授权时照样列出，调用时如实说原因（扩展自带 Unavailable）
        if (listed.Contains(BuiltinExtensions.Desktop))
        {
            builtins.Add(resources.Desktop == null
                ? BuiltinExtensions.Unavailable(BuiltinExtensions.Desktop, "Desktop control is not available here.")
                : BuiltinExtensions.CreateDesktop(resources.Desktop, resources.SessionId, Path.Combine(
                    resources.OutputRoomDirectory.Length > 0 ? resources.OutputRoomDirectory : Path.GetTempPath(), "desktop"),
                    paths, imageMode == EReadImageMode.Attach && !mountFiles ? EReadImageMode.None : imageMode));
        }

        // 生图只看配置：至少配了一个生图模型，连不连得通不看（ADR 0052）
        if (listed.Contains(BuiltinExtensions.Image))
        {
            builtins.Add(BuiltinExtensions.CreateImage(paths, resources.ImageProgress, resources.ImageModelsConfigured));
        }

        if (listed.Contains(BuiltinExtensions.Audio)) builtins.Add(BuiltinExtensions.CreateAudio(paths, resources.Character));
        return builtins;
    }

    /// <summary>
    /// 角色没禁的内置扩展名（系统提示的名单与这里同一个判据）
    /// </summary>
    /// <param name="config">生效能力</param>
    /// <returns>扩展名，按名单顺序</returns>
    internal static IEnumerable<string> ListedBuiltins(AgentToolConfig config)
    {
        HashSet<string> disabled = new(config.DisabledExtensions, StringComparer.OrdinalIgnoreCase);
        return BuiltinExtensions.Names.Where(name => !disabled.Contains(name));
    }
}
