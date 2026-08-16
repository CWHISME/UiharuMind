using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 智能体档的能力面板数据：工具开关 + 技能勾选。<b>直接读写角色身上那份</b>
/// <see cref="AgentToolConfig"/>，没有中间副本——它曾经是全局设置，现在唯一权威就是角色存档。
/// 保存由外层编辑窗统一做（改动只落在内存里的角色对象上）。
/// </summary>
public sealed class AgentToolViewData
{
    /// <summary>工具开关(按装配顺序排,与提示词里纪律段的次序一致)</summary>
    public ObservableCollection<AgentToolToggle> Toggles { get; } = new();

    /// <summary>技能勾选项;技能包一个都没有时为空</summary>
    public ObservableCollection<AgentSkillToggle> Skills { get; } = new();

    /// <summary>MCP server 勾选项;一个都没配时为空</summary>
    public ObservableCollection<AgentMcpServerToggle> McpServers { get; } = new();

    /// <summary>技能列表为空(界面据此显示空态文案)</summary>
    public bool HasNoSkills => Skills.Count == 0;

    /// <summary>没有配置任何 MCP server(界面据此显示空态文案)</summary>
    public bool HasNoMcpServers => McpServers.Count == 0;

    /// <summary>
    /// 绑定到一份能力配置
    /// </summary>
    /// <param name="tools">角色身上那份能力配置</param>
    /// <param name="snapshot">
    /// 当前会话实际挂上的那份快照，用来给每一档标估算占用。
    /// 从角色库打开编辑页时没有会话，传 null——那时占用一律显示「—」，
    /// 因为「关掉能省多少」这个问题在没有运行期上下文时确实答不出（识图工具挂不挂取决于模型）。
    /// </param>
    public AgentToolViewData(AgentToolConfig tools, AgentCapabilitySnapshot? snapshot = null)
    {
        string L(string key) => LocalizationManager.Instance.GetString(key);

        // 有快照才给数;没有就是 null,界面显示「—」。归属由装配现场登记,不靠名字反推
        int? Tokens(EAgentCapability capability) =>
            snapshot?.TokensByCapability.TryGetValue(capability, out int value) == true ? value : null;

        Toggles.Add(new AgentToolToggle(L("AgentSettingCapFileAccess"), L("AgentGateDescFileAccess"),
            () => tools.EnableFileAccess, v => tools.EnableFileAccess = v,
            Tokens(EAgentCapability.FileAccess)));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapShellExecution"), L("AgentGateDescShell"),
            () => tools.EnableShellExecution, v => tools.EnableShellExecution = v,
            Tokens(EAgentCapability.Shell)));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapWebSearch"), L("AgentGateDescWebSearch"),
            () => tools.EnableWebSearch, v => tools.EnableWebSearch = v,
            Tokens(EAgentCapability.WebSearch)));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapVisionTool"), L("AgentGateDescVisionTool"),
            () => tools.EnableVisionTool, v => tools.EnableVisionTool = v,
            Tokens(EAgentCapability.VisionTool)));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapKnowledgeSearchTool"), L("AgentGateDescKnowledgeSearch"),
            () => tools.EnableKnowledgeSearchTool, v => tools.EnableKnowledgeSearchTool = v,
            Tokens(EAgentCapability.KnowledgeSearch)));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapSubAgent"), L("AgentGateDescSubAgent"),
            () => tools.EnableSubAgent, v => tools.EnableSubAgent = v,
            Tokens(EAgentCapability.SubAgent)));
        // 下面四档不挂工具(框架 provider 或记忆存储),没有工具定义可算,故不给占用
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapFileMemory"), L("AgentGateDescFileMemory"),
            () => tools.EnableFileMemory, v => tools.EnableFileMemory = v));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapScheduledTasks"), L("AgentGateDescScheduledTasks"),
            () => tools.EnableScheduledTasks, v => tools.EnableScheduledTasks = v,
            Tokens(EAgentCapability.ScheduledTasks)));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapTodoList"), L("AgentGateDescTodoList"),
            () => tools.EnableTodoList, v => tools.EnableTodoList = v));
        Toggles.Add(new AgentToolToggle(L("AgentSettingCapAgentMode"), L("AgentGateDescAgentMode"),
            () => tools.EnableAgentMode, v => tools.EnableAgentMode = v));

        // MCP server 名单是内存里的配置,同步取即可(工具数与占用取常驻缓存,不等网络)
        foreach (McpServerConfig server in McpManager.Instance.GetServers()
                     .OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            McpServers.Add(new AgentMcpServerToggle(server,
                McpManager.Instance.GetServerStatus(server.Name), tools));
        }

        _ = LoadSkillsAsync(tools);
    }

    private async Task LoadSkillsAsync(AgentToolConfig tools)
    {
        // 技能要读盘解析,不阻塞面板构造
        List<SkillCatalogEntry> entries = await SkillCatalog.Instance.GetEntriesAsync();
        foreach (SkillCatalogEntry entry in entries.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            Skills.Add(new AgentSkillToggle(entry, tools));
        }
    }
}

/// <summary>
/// 一个工具开关。读写直穿角色那份配置，不留副本——留副本就会出现"面板显示开着、存档里是关着"。
/// </summary>
public sealed partial class AgentToolToggle : ObservableObject
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;

    /// <summary>开关名</summary>
    public string Name { get; }

    /// <summary>说明(悬停显示)</summary>
    public string Description { get; }

    /// <inheritdoc cref="CapabilityTokens.Format"/>
    public int? EstimatedTokens { get; }

    /// <summary>token 文案；未知时为 <c>—</c></summary>
    public string TokenText => CapabilityTokens.Format(EstimatedTokens);

    /// <summary>是否启用</summary>
    public bool IsEnabled
    {
        get => _get();
        set
        {
            if (_get() == value) return;
            _set(value);
            OnPropertyChanged();
        }
    }

    public AgentToolToggle(string name, string description, Func<bool> get, Action<bool> set,
        int? estimatedTokens = null)
    {
        Name = name;
        Description = description;
        _get = get;
        _set = set;
        EstimatedTokens = estimatedTokens;
    }
}

/// <summary>
/// 能力项的 token 文案。工具开关与黑名单勾选两条继承线都要它，故收在此处一份。
/// </summary>
public static class CapabilityTokens
{
    /// <summary>
    /// 格式化估算占用。<b>这是「预估」，与右栏那份「本会话实际」不是同一个数字</b>——
    /// 编辑页读的是配置，拿不到运行期条件（识图工具在视觉模型下不挂、server 没连上就没有 schema），
    /// 所以只有从当前会话取到快照时才有值。
    ///
    /// 未知时给 <c>—</c> 而不是 <c>0</c>：0 看起来像个确定的答案，而这里是「不知道」。
    /// </summary>
    /// <param name="tokens">估算值；null 表示未知</param>
    /// <returns>展示文案</returns>
    public static string Format(int? tokens) => tokens is { } value ? $"~{value}" : "—";
}

/// <summary>
/// 一个"按名字禁用"的勾选项：配置里存的是<b>禁用</b>清单，所以勾选即"从禁用清单里移除"。
///
/// 技能与 MCP server 是同一套语义（见 <see cref="AgentToolConfig.DisabledSkills"/> 与
/// <see cref="AgentToolConfig.DisabledMcpServers"/>），差别只在各自的展示字段上，
/// 因此这段读写收在此处一份，派生类只补自己那点信息。
/// </summary>
public abstract partial class AgentBlacklistToggle : ObservableObject
{
    private readonly List<string> _disabled;

    /// <summary>条目名(即黑名单里存的那个字符串)</summary>
    public string Name { get; }

    /// <summary>说明</summary>
    public string Description { get; }

    /// <summary>本智能体是否可用该条目</summary>
    public bool IsEnabled
    {
        get => !_disabled.Any(x => string.Equals(x, Name, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (IsEnabled == value) return;
            if (value) _disabled.RemoveAll(x => string.Equals(x, Name, StringComparison.OrdinalIgnoreCase));
            else _disabled.Add(Name);
            OnPropertyChanged();
        }
    }

    /// <inheritdoc cref="CapabilityTokens.Format"/>
    public int? EstimatedTokens { get; protected init; }

    /// <summary>token 文案；未知时为 <c>—</c></summary>
    public string TokenText => CapabilityTokens.Format(EstimatedTokens);

    protected AgentBlacklistToggle(string name, string description, List<string> disabled)
    {
        Name = name;
        Description = description;
        _disabled = disabled;
    }
}

/// <summary>
/// 一个技能勾选项
/// </summary>
public sealed class AgentSkillToggle : AgentBlacklistToggle
{
    /// <summary>框架是否接受加载(未加载的勾了也不生效,界面据此禁用)</summary>
    public bool IsLoaded { get; }

    /// <summary>是否退出了模型自选(只能点名调用)</summary>
    public bool IsUserInvokedOnly { get; }

    public AgentSkillToggle(SkillCatalogEntry entry, AgentToolConfig tools)
        : base(entry.Name, entry.Description, tools.DisabledSkills)
    {
        IsLoaded = entry.IsLoaded;
        IsUserInvokedOnly = !entry.IsModelInvocable;
        // 常驻的只有广告行(名字 + 描述):技能正文按需装载,算进来会误导人去关一个几乎不占位的技能
        EstimatedTokens = ToolTokenEstimator.EstimateText(entry.Name) +
                          ToolTokenEstimator.EstimateText(entry.Description);
    }
}

/// <summary>
/// 一个 MCP server 勾选项。
///
/// 勾选决定的是<b>本智能体能不能用它的工具</b>，与设置页那个「是否托管」是两码事：
/// 后者管的是要不要为它拉起一个进程（连接层），这里管的是能力（见 ADR 0007）。
/// 因此没托管的 server 照样列在这里——托管起来之后本角色就会立刻吃到它。
/// </summary>
public sealed class AgentMcpServerToggle : AgentBlacklistToggle
{
    /// <summary>server 当前是否托管中(界面据此提示"配了但没开")</summary>
    public bool IsHosted { get; }

    /// <summary>已取回的工具数;未连上时为 0</summary>
    public int ToolCount { get; }

    public AgentMcpServerToggle(McpServerConfig server, McpServerStatus status, AgentToolConfig tools)
        : base(server.Name, DescribeTransport(server), tools.DisabledMcpServers)
    {
        IsHosted = server.IsEnabled;
        ToolCount = status.ToolCount;
        // 没连上就算不出:schema 才是占用的主体,而 schema 得连上才有。此时显示「—」而不是 0
        EstimatedTokens = status.EstimatedTokens;
    }

    private static string DescribeTransport(McpServerConfig server)
    {
        return server.TransportType == EMcpTransportType.Http
            ? server.Url
            : $"{server.Command} {string.Join(' ', server.Args)}".Trim();
    }
}
