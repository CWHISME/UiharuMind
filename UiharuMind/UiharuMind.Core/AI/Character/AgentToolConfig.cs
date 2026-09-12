namespace UiharuMind.Core.AI.Character;

/// <summary>
/// 文件记忆的归属范围。
///
/// 两档是<b>同一棵树的两层</b>：角色级是 <c>FileMemory/{角色}_{id}/</c>，
/// 项目级是它下面再多一段工作区。角色在外层是刻意的——ADR 0002 的核心性质是
/// 「角色 A 的笔记不能漏进角色 B」，反过来嵌套就把这条丢了，且改名对账要改成遍历每个工作区。
/// </summary>
public enum EFileMemoryScope
{
    /// <summary>按角色：同一角色换会话、换工作区都记得（ADR 0002 的原始行为）</summary>
    Character,

    /// <summary>
    /// 按角色 × 工作区：同一角色在不同项目里各记一份。
    /// <b>没绑工作区时回落角色级</b>——绝不能落进 Scratch，那是可丢弃的缓存树（ADR 0013）。
    /// </summary>
    Workspace,
}

/// <summary>
/// 一个智能体的能力配置：装哪些工具、禁用哪些技能。<b>只对
/// <see cref="ECharacterKind.Agent"/> 有意义</b>，其余档位一律不装工具。
///
/// 它长在角色身上而不是全局设置里：一个只读的调研智能体与一个能改文件的开发智能体
/// 该带不同的工具集，而全局开关做不到这件事。运行时<b>只读这一份</b>——刻意不留"全局总闸"，
/// 两层 AND 会让「角色开了却没挂上」要查两处；执行安全由权限档兜（见 ADR 0003）。
///
/// 字段默认值即"新建智能体时的默认能力"，与下沉前那批全局开关的默认值一致。
/// </summary>
public class AgentToolConfig
{
    /// <summary>启用文件操作工具(Read/Write/Edit/Glob/Grep)。删文件走 shell，没有专门的工具</summary>
    public bool EnableFileAccess { get; set; } = true;

    /// <summary>启用 Shell 执行工具</summary>
    public bool EnableShellExecution { get; set; } = true;

    /// <summary>启用网络搜索工具(web_search/web_fetch)</summary>
    public bool EnableWebSearch { get; set; } = true;

    /// <summary>启用文件记忆(框架 FileMemoryProvider,agent 自记的笔记,按角色分目录跨会话共享)</summary>
    public bool EnableFileMemory { get; set; } = true;

    /// <summary>
    /// 文件记忆的归属范围。默认<see cref="EFileMemoryScope.Character"/>——那正是现存记忆
    /// 所在的路径，因此这一档对已有数据完全无感（见 ADR 0002 修正案）。
    /// </summary>
    public EFileMemoryScope FileMemoryScope { get; set; } = EFileMemoryScope.Character;

    /// <summary>启用定时任务工具(ScheduleTask)</summary>
    public bool EnableScheduledTasks { get; set; } = true;

    /// <summary>启用识图工具(ViewImage,委托视觉模型答图片问题)</summary>
    public bool EnableVisionTool { get; set; } = true;

    /// <summary>启用知识库检索工具(KnowledgeSearch,检索会话挂载的嵌入知识库)</summary>
    public bool EnableKnowledgeSearchTool { get; set; }

    /// <summary>启用子代理工具(RunSubAgent,把探查委派给子代理,过程不吃主上下文)</summary>
    public bool EnableSubAgent { get; set; } = true;

    /// <summary>启用任务清单(框架 TodoProvider;关闭时对话侧栏的任务清单同步隐藏)</summary>
    public bool EnableTodoList { get; set; }

    /// <summary>启用计划模式(框架 AgentModeProvider 的 plan/execute;关闭时输入框的模式切换同步隐藏)</summary>
    public bool EnableAgentMode { get; set; }

    /// <summary>
    /// 本智能体禁用的技能名(SKILL.md 技能目录名)。技能与工具同类，都是"这个智能体有什么能力"，
    /// 所以同样按角色配。
    /// </summary>
    public List<string> DisabledSkills { get; set; } = new();

    /// <summary>
    /// 本智能体禁用的 MCP server 名。
    ///
    /// MCP 工具此前是全局无差别下发的——只要 server 托管着，每个智能体都吃下它的全部工具，
    /// 是本类"按角色配能力"这条规矩下唯一的例外。收进来之后，
    /// <c>McpServerConfig.IsEnabled</c> 退回到纯连接层（要不要托管这个进程），
    /// 能力层只有这一份名单说话，因此不构成 ADR 0003 反对的两层 AND（见 ADR 0008）。
    ///
    /// 取<b>黑名单</b>而非白名单，与 <see cref="DisabledSkills"/> 同形：空名单即全给。
    /// </summary>
    public List<string> DisabledMcpServers { get; set; } = new();

    /// <summary>
    /// 与另一份配置取交集（逐项与）。用于子智能体：委派出去的那一个<b>不能比派活的这一个能力更大</b>，
    /// 否则挂一个开着 shell 的子智能体，就等于给关掉了 shell 的父智能体开了后门。
    /// </summary>
    /// <param name="other">另一份配置</param>
    /// <returns>新的配置实例（两边都开才开）</returns>
    public AgentToolConfig Intersect(AgentToolConfig other)
    {
        return new AgentToolConfig
        {
            EnableFileAccess = EnableFileAccess && other.EnableFileAccess,
            EnableShellExecution = EnableShellExecution && other.EnableShellExecution,
            EnableWebSearch = EnableWebSearch && other.EnableWebSearch,
            EnableFileMemory = EnableFileMemory && other.EnableFileMemory,
            // 范围不是"能力大小"而是位置,取隔离更强的那一侧:与"子代理不能比父代理能力更大"同向,
            // 父代理按项目隔离时,子代理不该把笔记写到跨项目共享的那一层去
            FileMemoryScope = FileMemoryScope == EFileMemoryScope.Workspace
                              || other.FileMemoryScope == EFileMemoryScope.Workspace
                ? EFileMemoryScope.Workspace
                : EFileMemoryScope.Character,
            EnableScheduledTasks = EnableScheduledTasks && other.EnableScheduledTasks,
            EnableVisionTool = EnableVisionTool && other.EnableVisionTool,
            EnableKnowledgeSearchTool = EnableKnowledgeSearchTool && other.EnableKnowledgeSearchTool,
            EnableSubAgent = EnableSubAgent && other.EnableSubAgent,
            EnableTodoList = EnableTodoList && other.EnableTodoList,
            EnableAgentMode = EnableAgentMode && other.EnableAgentMode,
            // 禁用清单取并集:任一侧禁掉的技能都不该出现
            DisabledSkills = DisabledSkills.Union(other.DisabledSkills, StringComparer.OrdinalIgnoreCase).ToList(),
            // 同理:父代理禁掉的 server,不能靠委派给子代理绕回来
            DisabledMcpServers = DisabledMcpServers
                .Union(other.DisabledMcpServers, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }
}
