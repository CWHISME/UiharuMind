/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

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
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 右栏「能力」页签的数据：本会话此刻到底挂了什么工具、哪些技能可用、接了哪些 MCP server。
///
/// <b>只读</b>。要改就去角色编辑页——那份黑名单只有一个写入口，在会话侧再开一个的话，
/// 「我只是这一次不想用」会静默变成永久修改，而两处 UI 写同一份数据也迟早对不上。
///
/// 数据源是装配产物本身（见 <see cref="AgentCapabilitySnapshot"/>），不是能力开关的二次推导。
/// 顺带把每一项的估算 token 摆出来：工具定义每轮都完整重发，本地模型的窗口常常只有几 K，
/// 这笔账看不见的时候，用户只会觉得"模型变笨了"。
/// </summary>
public sealed partial class ConversationCapabilityViewData : ObservableObject
{
    /// <summary>自建工具</summary>
    public ObservableCollection<CapabilityItem> Tools { get; } = new();

    /// <summary>可用技能</summary>
    public ObservableCollection<CapabilityItem> Skills { get; } = new();

    /// <summary>MCP server 分组</summary>
    public ObservableCollection<McpServerGroupItem> McpServers { get; } = new();

    /// <summary>全部工具定义的估算 token 合计</summary>
    [ObservableProperty] private int _estimatedTokens;

    /// <summary>一项能力都没有(界面据此显示空态)</summary>
    [ObservableProperty] private bool _isEmpty = true;

    //================= 工作区卡片上的统计行 =================
    // 每档只给「有多少、占多少」这个结论,明细在本页签里。数量与占用一并给:
    // 光有数量说明不了轻重(一个 20 参数的工具比五个无参工具还贵)。
    // 只存数值那一半,标签由界面按本地化取——竖排时标签要单独占一列才对得齐

    [ObservableProperty] private string _toolStat = string.Empty;
    [ObservableProperty] private string _skillStat = string.Empty;
    [ObservableProperty] private string _mcpStat = string.Empty;

    /// 空的那一档整行不出：既占一行又什么都没说
    [ObservableProperty] private bool _hasTools;
    [ObservableProperty] private bool _hasSkills;
    [ObservableProperty] private bool _hasMcp;

    /// <summary>页签标题(带计数)。不点开也能知道里面有没有东西,默认选谁因此无关紧要</summary>
    [ObservableProperty] private string _tabHeader = string.Empty;

    /// 明细里的分节标题也带计数,与页签标题同理
    [ObservableProperty] private string _toolsHeader = string.Empty;
    [ObservableProperty] private string _skillsHeader = string.Empty;

    /// <summary>合计占用文案：有上限时形如 <c>5.1k / 8192</c>，上限未知时只给占用</summary>
    [ObservableProperty] private string _totalText = string.Empty;

    /// <summary>占用已超过上下文上限的告警比例（上限未知时恒为 false）</summary>
    [ObservableProperty] private bool _isOverBudget;

    /// 超过上下文上限这个比例就标黄。工具定义是每轮的固定开销,
    /// 吃掉四成窗口意味着历史只剩六成可用,而那正是对话变笨的起点
    private const double BudgetWarnFraction = 0.4;

    /// <summary>
    /// 按当前会话重新取一份快照
    /// </summary>
    /// <param name="runner">当前会话的执行器；为空则清空</param>
    /// <param name="character">当前会话的角色（技能过滤按它的禁用清单）</param>
    /// <param name="contextLength">当前模型的上下文上限；0 表示未知，此时不给分母也不告警</param>
    public async Task RefreshAsync(ICharacterRunner? runner, CharacterData? character, int contextLength)
    {
        Tools.Clear();
        Skills.Clear();
        McpServers.Clear();

        // 切快照要为每个工具的 schema 分词,放后台——与输入框那个字数统计同样的处置
        AgentCapabilitySnapshot snapshot = runner == null
            ? AgentCapabilitySnapshot.Empty
            : await Task.Run(runner.GetCapabilities);

        // 按占用降序：这一栏的用途就是「该关掉哪个」，贵的必须在最上面。
        // 装配顺序在这里没有意义（那是提示词纪律段的次序，与成本无关）
        foreach (AgentToolInfo tool in snapshot.BuiltInTools.OrderByDescending(x => x.EstimatedTokens))
        {
            Tools.Add(new CapabilityItem(tool.Name, tool.Description, tool.EstimatedTokens));
        }

        foreach (McpServerToolGroup group in snapshot.Mcp.Groups)
        {
            McpServers.Add(new McpServerGroupItem(group, McpManager.Instance.GetServerStatus(group.ServerName)));
        }

        // 技能不进工具集(框架按需装载),所以另取一次;口径与点名调用那处一致
        if (character != null && character.Kind.IsAgent())
        {
            IReadOnlyList<SkillCatalogEntry> entries =
                await SkillCatalog.Instance.GetInvocableEntriesAsync(character.Tools.DisabledSkills);
            // 常驻的只有广告行(名字 + 描述):技能正文是按需装载的,
            // 把正文算进来会让人去关一个其实几乎不占位的技能
            foreach (CapabilityItem item in entries
                         .Select(x => new CapabilityItem(x.Name, x.Description,
                             ToolTokenEstimator.EstimateText(x.Name) +
                             ToolTokenEstimator.EstimateText(x.Description)))
                         .OrderByDescending(x => x.EstimatedTokens))
            {
                Skills.Add(item);
            }
        }

        EstimatedTokens = snapshot.EstimatedTokens + Skills.Sum(x => x.EstimatedTokens);
        IsEmpty = Tools.Count == 0 && Skills.Count == 0 && McpServers.Count == 0;
        RefreshBadges(contextLength);
    }

    /// 三个徽章、页签标题与合计文案都从当前集合派生,一处算完
    private void RefreshBadges(int contextLength)
    {
        string L(string key) => LocalizationManager.Instance.GetString(key);

        HasTools = Tools.Count > 0;
        HasSkills = Skills.Count > 0;
        HasMcp = McpServers.Count > 0;

        ToolStat = FormatStat(Tools.Count, Tools.Sum(x => x.EstimatedTokens));
        SkillStat = FormatStat(Skills.Count, Skills.Sum(x => x.EstimatedTokens));
        McpStat = FormatStat(McpServers.Count, McpServers.Sum(x => x.TotalEstimatedTokens));

        TabHeader = IsEmpty
            ? L("AgentCapabilityTitle")
            : $"{L("AgentCapabilityTitle")} {Tools.Count + Skills.Count + McpServers.Sum(x => x.Tools.Count)}";
        ToolsHeader = $"{L("AgentCapabilityTools")} {Tools.Count}";
        SkillsHeader = $"{L("AgentCapabilitySkills")} {Skills.Count}";

        TotalText = contextLength > 0
            ? $"{FormatTokens(EstimatedTokens)} / {FormatTokens(contextLength)}"
            : FormatTokens(EstimatedTokens);
        IsOverBudget = contextLength > 0 && EstimatedTokens > contextLength * BudgetWarnFraction;
    }

    /// 形如 <c>9 · 1.8k</c>（数量 · 占用）;那一档为空时整行不出,故不必处理零值
    private static string FormatStat(int count, int tokens)
    {
        return $"{count} · {FormatTokens(tokens)}";
    }

    private static string FormatTokens(int tokens)
    {
        return tokens >= 1000 ? $"{tokens / 1000.0:0.#}k" : tokens.ToString();
    }
}

/// <summary>
/// 一项能力（工具或技能）的展示条目
/// </summary>
/// <param name="Name">名字</param>
/// <param name="Description">描述</param>
/// <param name="EstimatedTokens">估算 token 数；技能为 0（按需装载，不常驻上下文）</param>
public sealed record CapabilityItem(string Name, string Description, int EstimatedTokens)
{
    /// <summary>是否显示 token 标记</summary>
    public bool HasTokenCost => EstimatedTokens > 0;

    /// <summary>token 标记文案。不带单位——单位在分节标题上说一次就够，右栏没有那个宽度重复它</summary>
    public string TokenText => $"~{EstimatedTokens}";
}

/// <summary>
/// 一个 MCP server 在本会话里的展示条目
/// </summary>
public sealed class McpServerGroupItem
{
    /// <summary>server 名</summary>
    public string Name { get; }

    /// <summary>该 server 贡献的工具</summary>
    public IReadOnlyList<CapabilityItem> Tools { get; }

    /// <summary>连接状态与自述占用的合计文案</summary>
    public string SummaryText { get; }

    /// <summary>自述是否已注入(它同样占 token,不该藏着)</summary>
    public bool InstructionsInjected { get; }

    /// <summary>被改过名的工具存在时的提示(撞名消歧)</summary>
    public bool HasRenamedTools { get; }

    /// <summary>本 server 的估算 token 合计(工具定义 + 已注入的自述)</summary>
    public int TotalEstimatedTokens { get; }

    public McpServerGroupItem(McpServerToolGroup group, McpServerStatus status)
    {
        Name = group.ServerName;
        Tools = group.Tools
            .Select(x => new CapabilityItem(FormatToolName(x), x.Description, x.EstimatedTokens))
            .ToList();
        InstructionsInjected = group.InstructionsInjected;
        HasRenamedTools = group.Tools.Any(x => x.IsRenamed);
        TotalEstimatedTokens = group.TotalEstimatedTokens;
        SummaryText = $"{status.State} · {group.Tools.Count} tools · ~{group.TotalEstimatedTokens} tok";
    }

    /// 改过名的把原名一并写出来:否则用户在 server 文档里按原名找不到对应项
    private static string FormatToolName(McpToolInfo tool)
    {
        return tool.IsRenamed ? $"{tool.Name}  ({tool.OriginalName})" : tool.Name;
    }
}
