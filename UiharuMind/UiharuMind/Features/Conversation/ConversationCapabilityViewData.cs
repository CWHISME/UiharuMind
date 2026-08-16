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
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 右栏「能力」页签与工作区卡片统计的数据：本会话此刻到底挂了什么工具、哪些技能可用、
/// 接了哪些 MCP server，以及<b>真正发出去的那段系统提示</b>按段各占多少。
///
/// <b>只读</b>。要改就去角色编辑页——那份黑名单只有一个写入口，在会话侧再开一个的话，
/// 「我只是这一次不想用」会静默变成永久修改，而两处 UI 写同一份数据也迟早对不上。
///
/// 数据源是装配产物本身（见 <see cref="AgentCapabilitySnapshot"/>），不是能力开关的二次推导。
/// 顺带把每一项的估算 token 摆出来：<b>系统提示与工具定义每轮都完整重发</b>，
/// 本地模型的窗口常常只有几 K，这笔账看不见的时候，用户只会觉得"模型变笨了"。
/// 提示词那一半尤其藏得深——一份角色卡加一份项目 AGENTS.md 轻松几千 token，
/// 而在它进账之前，这里报的合计只有工具那一半。
/// </summary>
public sealed partial class ConversationCapabilityViewData : ObservableObject
{
    /// <summary>自建工具</summary>
    public ObservableCollection<CapabilityItem> Tools { get; } = new();

    /// <summary>可用技能</summary>
    public ObservableCollection<CapabilityItem> Skills { get; } = new();

    /// <summary>MCP server 分组</summary>
    public ObservableCollection<McpServerGroupItem> McpServers { get; } = new();

    /// <summary>每轮固定开销的估算合计：系统提示 + 工具定义 + 技能广告行</summary>
    [ObservableProperty] private int _estimatedTokens;

    /// <summary>一项能力都没有(界面据此显示空态)</summary>
    [ObservableProperty] private bool _isEmpty = true;

    /// <summary>
    /// 系统提示的分段明细（角色段、工具纪律、MCP 自述、工作区规矩），每段可看全文。
    /// 这一栏原先只报了工具那一半，而提示词同样每轮完整重发，且通常比工具还大
    /// </summary>
    public ObservableCollection<PromptSegmentItem> PromptSegments { get; } = new();

    /// <summary>提示词分节是否有内容</summary>
    [ObservableProperty] private bool _hasPromptSegments;

    //================= 工作区卡片上的统计行 =================
    // 每档只给「占多少」这个结论,明细在本页签里。原先还带一个数量前缀,补进提示词之后废了:
    // 角色提示是一段文本、AGENTS.md 是一个文件,都没有数量可言,五行里两行空着那一列就没意义了。
    // 数量在别处已经报过三遍(页签标题、工具分节、技能分节),摘要只需回答「谁贵」

    /// <summary>
    /// 五档统计（角色提示 / 工作区 / 工具 / 技能 / MCP），空的那一档不进集合。
    /// 做成集合而不是十个属性：档数还会变，而每加一档就加「文案 + 有没有」两个属性的写法，
    /// 到第五档就已经是十个属性伺候一个竖排列表了
    /// </summary>
    public ObservableCollection<CapabilityStatItem> Stats { get; } = new();

    /// <summary>统计块是否有内容（空态整块不出，见下方 <see cref="RefreshSummary"/> 的说明）</summary>
    [ObservableProperty] private bool _hasStats;

    /// 空的那一档整行不出：既占一行又什么都没说
    [ObservableProperty] private bool _hasTools;
    [ObservableProperty] private bool _hasSkills;

    /// <summary>
    /// 页签标题(带计数)。不点开也能知道里面有没有东西,默认选谁因此无关紧要。
    /// <b>初值就得是有效文案</b>：智能体页的会话是懒建的，首轮发送前没有执行器、刷新一次都不会跑，
    /// 初值留空串的话页签标题在那之前是一片空白
    /// </summary>
    [ObservableProperty] private string _tabHeader = LocalizationManager.Instance.GetString("AgentCapabilityTitle");

    /// 明细里的分节标题也带计数,与页签标题同理
    [ObservableProperty] private string _toolsHeader = string.Empty;
    [ObservableProperty] private string _skillsHeader = string.Empty;

    /// <summary>合计占用文案：有上限时形如 <c>5.1k / 8192</c>，上限未知时只给占用</summary>
    [ObservableProperty] private string _totalText = string.Empty;

    /// <summary>固定开销已经吃到压缩水位的告警（上限未知时恒为 false）</summary>
    [ObservableProperty] private bool _isOverBudget;

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
        PromptSegments.Clear();

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

        foreach (AgentPromptSegment segment in snapshot.PromptSegments)
        {
            PromptSegments.Add(new PromptSegmentItem(SectionLabel(segment.Section), segment.EstimatedTokens,
                segment.Text, segment.CountsTowardTotal));
        }

        IsEmpty = Tools.Count == 0 && Skills.Count == 0 && McpServers.Count == 0;
        RefreshSummary(snapshot, contextLength);
    }

    /// <summary>
    /// 五档统计、页签标题与合计文案都从当前集合派生，一处算完。
    ///
    /// <b>合计就是这五档之和</b>，不另取一条路算——摆在同一块上的分项与合计对不上，
    /// 是这种小面板最容易犯又最伤信任的错。
    /// </summary>
    /// <param name="snapshot">本次快照（提示词分段的占用只在它身上）</param>
    /// <param name="contextLength">当前模型的上下文上限；0 表示未知</param>
    private void RefreshSummary(AgentCapabilitySnapshot snapshot, int contextLength)
    {
        string L(string key) => LocalizationManager.Instance.GetString(key);

        HasTools = Tools.Count > 0;
        HasSkills = Skills.Count > 0;

        int characterTokens = snapshot.PromptTokensOf(EPromptSection.Character);
        int workspaceTokens = snapshot.PromptTokensOf(EPromptSection.Workspace);
        // 工具纪律段并进「工具」这一档:那段正文是按能力开关派生的,关掉文件工具,
        // 纪律里那一节和那几个工具定义一起消失——两者同生同死,拆成两行等于让人看两个数做一个决定
        int toolTokens = Tools.Sum(x => x.EstimatedTokens) + snapshot.PromptTokensOf(EPromptSection.ToolDisciplines);
        int skillTokens = Skills.Sum(x => x.EstimatedTokens);
        int mcpTokens = McpServers.Sum(x => x.TotalEstimatedTokens);

        Stats.Clear();
        AddStat(L("AgentCapabilityCharacterPrompt"), characterTokens);
        AddStat(L("AgentCapabilityWorkspaceRule"), workspaceTokens);
        AddStat(L("AgentCapabilityTools"), toolTokens);
        AddStat(L("AgentCapabilitySkills"), skillTokens, L("AgentCapabilitySkillResidentTip"));
        AddStat("MCP", mcpTokens);
        HasStats = Stats.Count > 0;

        EstimatedTokens = characterTokens + workspaceTokens + toolTokens + skillTokens + mcpTokens;

        TabHeader = IsEmpty
            ? L("AgentCapabilityTitle")
            : $"{L("AgentCapabilityTitle")} {Tools.Count + Skills.Count + McpServers.Sum(x => x.Tools.Count)}";
        ToolsHeader = $"{L("AgentCapabilityTools")} {Tools.Count}";
        SkillsHeader = $"{L("AgentCapabilitySkills")} {Skills.Count}";
        HasPromptSegments = PromptSegments.Count > 0;

        TotalText = contextLength > 0
            ? $"{FormatTokens(EstimatedTokens)} / {FormatTokens(contextLength)}"
            : FormatTokens(EstimatedTokens);
        IsOverBudget = ExceedsCompactionWatermark(EstimatedTokens, contextLength);
    }

    /// 空的那一档不进集合:既占一行又什么都没说
    private void AddStat(string label, int tokens, string? tip = null)
    {
        if (tokens <= 0) return;
        Stats.Add(new CapabilityStatItem(label, FormatTokens(tokens), tip));
    }

    /// <summary>
    /// 固定开销是否已经吃到工具结果折叠的水位。
    ///
    /// 判据不自造比例，直接用 <see cref="HistoryCompaction"/> 的那两个常数换算——
    /// 这个告警要说的是一件真事：<b>一句话还没说，压缩就已经在门口了</b>。
    /// 同一个应用里讲「什么时候开始丢上下文」，不该有第二把尺子（另一把在 ContextUsageViewData）。
    /// </summary>
    /// <param name="tokens">固定开销</param>
    /// <param name="contextLength">上下文上限；0 表示未知，此时不告警</param>
    /// <returns>已到水位返回 true</returns>
    private static bool ExceedsCompactionWatermark(int tokens, int contextLength)
    {
        if (contextLength <= 0) return false;
        return tokens >= HistoryCompaction.InputBudgetFor(contextLength) * HistoryCompaction.ToolEvictionThreshold;
    }

    /// 段别的显示名。段别是 Core 的枚举，文案是 UI 的事，映射只此一处
    private static string SectionLabel(EPromptSection section)
    {
        return LocalizationManager.Instance.GetString(section switch
        {
            EPromptSection.Character => "AgentCapabilityCharacterPrompt",
            EPromptSection.ToolDisciplines => "AgentCapabilityToolRules",
            EPromptSection.Mcp => "AgentCapabilityPromptMcp",
            _ => "AgentCapabilityWorkspaceRule",
        });
    }

    private static string FormatTokens(int tokens)
    {
        return tokens >= 1000 ? $"{tokens / 1000.0:0.#}k" : tokens.ToString();
    }
}

/// <summary>
/// 工作区卡片上的一行统计：一档占多少。
/// 标签在此处就取好本地化文案而不是留 key 给界面——五档里有一档（MCP）根本没有 key
/// </summary>
/// <param name="Label">档名</param>
/// <param name="TokenText">占用文案</param>
/// <param name="Tip">悬停说明；无则为 null（绑到 ToolTip.Tip 上即不显示）</param>
public sealed record CapabilityStatItem(string Label, string TokenText, string? Tip = null);

/// <summary>
/// 系统提示里的一段，在「能力」页签里的展示条目。点开看全文——
/// 这一栏存在的意义是回答「模型现在到底看得见什么」，而工具那半边一直是明细，提示词这半边不该只有一个数
/// </summary>
/// <param name="Label">段名</param>
/// <param name="EstimatedTokens">估算占用</param>
/// <param name="Text">该段发出去的原文</param>
/// <param name="CountsTowardTotal">是否计入合计；MCP 自述为 false（已在 MCP 那一档里计过）</param>
public sealed record PromptSegmentItem(string Label, int EstimatedTokens, string Text, bool CountsTowardTotal)
{
    /// <summary>占用文案。与工具行同一形状，单位在分节标题上说一次就够</summary>
    public string TokenText => $"~{EstimatedTokens}";

    /// <summary>不计入合计时的说明；否则为 null</summary>
    public string? Tip => CountsTowardTotal
        ? null
        : LocalizationManager.Instance.GetString("AgentCapabilityPromptNotCounted");
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
