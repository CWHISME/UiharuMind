/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using System.IO;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.SidePanels;

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

    /// <summary>
    /// MCP <b>预告</b>区：装配之前这个会话将会接入什么（见 <c>McpManager.GetPlannedServers</c>）。
    ///
    /// 与上面那个集合<b>刻意分开</b>：那个是装配产物（实况），这个是预测。混成一个列表的话，
    /// 本类开头那句「数据源是装配产物本身，不是能力开关的二次推导」就废了，
    /// 以后排查「面板说挂了但模型没有」会多一层。
    /// </summary>
    public ObservableCollection<McpPlannedServerItem> McpPlanned { get; } = new();

    /// <summary>
    /// 预告区是否显示。首轮发送前显示（那时实况是空的，否则新会话就是"不告而连"）；
    /// 装配之后<b>只要还有没挂上的</b>就继续显示——待确认、被覆盖、被禁用的那些若在此时消失，
    /// 用户就再也看不见「有东西因为没授权而没挂上」。
    /// </summary>
    [ObservableProperty] private bool _hasMcpPlanned;

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

    /// <summary>
    /// 统计块是否有内容。
    ///
    /// <b>初值为真</b>：切会话是换一个视图模型实例，这一份是新造的，而算出内容要等一次
    /// 后台预演。初值为假的话，每切一次会话整块都会先消失、几百毫秒后再出现——
    /// 那是最扎眼的一种闪。而它<b>几乎不会真为假</b>：这块只出现在智能体页，
    /// 智能体档必然带着角色提示与工具。真为空时（能力全关且无技能）刷新后自然收起
    /// </summary>
    [ObservableProperty] private bool _hasStats = true;

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

    /// <summary>
    /// 合计占用文案：有上限时形如 <c>5.1k / 8192</c>，上限未知时只给占用。
    /// 初值给一个破折号而不是空串——块从一开始就在（见 <see cref="HasStats"/>），
    /// 数还没算出来时那一行不该是半截空白
    /// </summary>
    [ObservableProperty] private string _totalText = "—";

    /// <summary>固定开销已经吃到压缩水位的告警（上限未知时恒为 false）</summary>
    [ObservableProperty] private bool _isOverBudget;

    /// 本次刷新的序号。切工作区与载入会话会同时触发刷新,而中间有 await——
    /// 靠它把作废的那一次挡在填充之前,否则列表里会出现两份同样的条目
    private int _refreshVersion;

    /// <summary>
    /// 按当前会话重新取一份快照
    /// </summary>
    /// <param name="runner">当前会话的执行器；为空则清空</param>
    /// <param name="character">当前会话的角色（技能过滤按它的禁用清单）</param>
    /// <param name="contextLength">当前模型的上下文上限；0 表示未知，此时不给分母也不告警</param>
    /// <param name="workspacePath">会话绑定的工作区；预告区据此读项目级 <c>.mcp.json</c></param>
    /// <param name="permissionModeIndex">权限档序号；会话还不存在时预演装配要用</param>
    public async Task RefreshAsync(ICharacterRunner? runner, CharacterData? character, int contextLength,
        string? workspacePath = null, int permissionModeIndex = 0)
    {
        int version = ++_refreshVersion;

        // 切快照要为每个工具的 schema 分词,放后台——与输入框那个字数统计同样的处置。
        // 没有执行者即会话还不存在(智能体页懒建):预演一次装配,首轮发送前也报得出
        // 角色提示/工作区规矩/工具这三档——它们全都只依赖角色与工作区,不依赖任何已发生的对话
        AgentCapabilitySnapshot snapshot = runner != null
            ? await Task.Run(runner.GetCapabilities)
            : character != null
                ? await Task.Run(() => CharacterRunnerFactory.Instance.PreviewCapabilitiesAsync(
                    AgentBuildProfile.FromDraft(character, workspacePath, permissionModeIndex)))
                : AgentCapabilitySnapshot.Empty;

        // 预告区要读工作区里的 .mcp.json,同样不在 UI 线程上做
        List<McpPlannedServer> planned = character != null && character.Kind.IsAgent()
            ? await Task.Run(() => McpManager.Instance.GetPlannedServers(
                workspacePath, character.Tools.DisabledMcpServers))
            : new List<McpPlannedServer>();

        IReadOnlyList<SkillCatalogEntry> skillEntries = character != null && character.Kind.IsAgent()
            ? await SkillCatalog.Instance.GetInvocableEntriesAsync(character.Tools.DisabledSkills)
            : [];

        // 期间又刷过一次:这一次的结果作废,让后来那次填。
        // 这个判据不能省——切工作区与载入会话会同时触发刷新,而清空与填充之间有 await,
        // 于是先跑那次的填充会落在后跑那次的清空之后,列表里出现两份同样的条目
        if (version != _refreshVersion) return;

        // 清空放在全部 await 之后:上面那条判据靠它才成立(先清空的话,
        // 一次作废的刷新仍会把界面清成空的)
        Tools.Clear();
        Skills.Clear();
        McpServers.Clear();
        McpPlanned.Clear();
        PromptSegments.Clear();

        // 按占用降序：这一栏的用途就是「该关掉哪个」，贵的必须在最上面。
        // 装配顺序在这里没有意义（那是提示词纪律段的次序，与成本无关）
        foreach (AgentToolInfo tool in snapshot.BuiltInTools.OrderByDescending(x => x.EstimatedTokens))
        {
            Tools.Add(new CapabilityItem(tool.Name, tool.Description, tool.EstimatedTokens));
        }

        foreach (McpServerToolGroup group in snapshot.Mcp.Groups)
        {
            McpServers.Add(new McpServerGroupItem(group,
                McpManager.Instance.GetServerStatus(group.ServerName, group.WorkspacePath)));
        }

        RefreshMcpPlanned(planned, mounted: snapshot.Mcp.Groups.Count);

        // 技能不进工具集(框架按需装载),所以另取一次;口径与点名调用那处一致。
        // 常驻的只有广告行(名字 + 描述):技能正文是按需装载的,
        // 把正文算进来会让人去关一个其实几乎不占位的技能
        foreach (CapabilityItem item in skillEntries
                     .Select(x => new CapabilityItem(x.Name, x.Description,
                         ToolTokenEstimator.EstimateText(x.Name) +
                         ToolTokenEstimator.EstimateText(x.Description)))
                     .OrderByDescending(x => x.EstimatedTokens))
        {
            Skills.Add(item);
        }

        foreach (AgentPromptSegment segment in snapshot.PromptSegments)
        {
            PromptSegments.Add(new PromptSegmentItem(SectionLabel(segment.Section), segment.EstimatedTokens,
                segment.Text, segment.CountsTowardTotal));
        }

        IsEmpty = Tools.Count == 0 && Skills.Count == 0 && McpServers.Count == 0 && McpPlanned.Count == 0;
        RefreshSummary(snapshot, contextLength);
    }

    /// <summary>
    /// 重建 MCP 预告区。
    ///
    /// 显示条件分两种情形，都不能省：
    /// <list type="bullet">
    /// <item><b>还没装配</b>（<paramref name="mounted"/> 为 0）——把整份将接入的名单摆出来。
    /// 这是本区块存在的理由：智能体会话的执行者懒建，首轮发送前实况栏是空的，
    /// 用户无从知道这个会话会自动连上什么。</item>
    /// <item><b>已装配但有条目没挂上</b>——只留那些没挂上的。待确认、被同名覆盖、被禁用的那几条
    /// 若在装配后消失，用户就再也看不见「有东西因为没授权而没挂上」。</item>
    /// </list>
    /// </summary>
    /// <param name="planned">已在后台取好的预告名单（非智能体档为空）</param>
    /// <param name="mounted">实况区已有的分组数；0 表示还没装配过</param>
    private void RefreshMcpPlanned(List<McpPlannedServer> planned, int mounted)
    {
        foreach (McpPlannedServer server in planned)
        {
            // 装配之后只留没挂上的:已经挂上的那些在实况区有更准的一份(含逐个工具与真实占用)
            if (mounted > 0 && server.WillBeMounted) continue;
            McpPlanned.Add(new McpPlannedServerItem(server));
        }

        HasMcpPlanned = McpPlanned.Count > 0;
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

        // MCP 那一档:装配之后用实况(准),装配之前用预告(估)。
        //
        // <b>装配前必须给个数</b>,不能留 0。MCP 是这几档里最贵的一笔——两个 server 就可能是四万
        // token,而本地模型的窗口常常只有 32K。此时卡片上写着「合计 1.6k」等于在撒谎:
        // 四万 token 的东西正等着在你按下发送的那一刻挂上去。
        // 顺带让下面那条压缩水位告警在首轮之前就能亮——那正是它最该亮的时候。
        int mcpTokens = McpServers.Sum(x => x.TotalEstimatedTokens);
        bool mcpIsForecast = false;
        if (mcpTokens == 0)
        {
            // 只算真会挂上的那些,且只算连过因而有账可依的(从未连过的 server 占多少是"不知道",不是 0)
            mcpTokens = McpPlanned.Sum(x => x.ForecastTokens);
            mcpIsForecast = mcpTokens > 0;
        }

        Stats.Clear();
        AddStat(L("AgentCapabilityCharacterPrompt"), characterTokens);
        AddStat(L("AgentCapabilityWorkspaceRule"), workspaceTokens);
        AddStat(L("AgentCapabilityTools"), toolTokens);
        AddStat(L("AgentCapabilitySkills"), skillTokens, L("AgentCapabilitySkillResidentTip"));
        // 估算的那一档要标出来:同一行数字,一个是实测一个是上次的账,不标就没法解释为什么会变
        AddStat(mcpIsForecast ? L("AgentCapabilityMcpForecast") : "MCP", mcpTokens,
            mcpIsForecast ? L("AgentCapabilityMcpForecastTip") : null);
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
    ///
    /// ⚠️ 这里比的是<b>输入预算</b>而不是历史额度，且这是对的：额度本身就等于预算减去固定开销
    /// （见 <see cref="HistoryCompaction.HistoryQuotaFor"/>），拿开销去比一个由它算出来的数是循环的。
    /// 「开销吃掉预算的一半」直白等价于「留给历史的额度已经不比开销多了」，正是要报的那件事。
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

/// <summary>
/// 预告区的一条：这个会话<b>将会</b>（或本该、却没能）接入的一个 server。
///
/// 只把 <see cref="McpPlannedServer"/> 折成界面要的几个字符串与布尔量，不做任何判断——
/// 「挂不挂得上」的口径在 Core 那一处算完了，这里再算一遍就是第二份真相。
/// </summary>
public sealed class McpPlannedServerItem
{
    /// <summary>server 名</summary>
    public string Name { get; }

    /// <summary>来源标签：项目级带上目录名，全局的直说是全局</summary>
    public string OriginText { get; }

    /// <summary>为什么没挂上 / 此刻什么状态；已连上的报工具数与占用</summary>
    public string StatusText { get; }

    /// <summary>将要执行的命令。这是安全相关的信息，必须能看见原文</summary>
    public string CommandLine { get; }

    /// <summary>待用户确认（界面用醒目色，它挡着一个本该可用的能力）</summary>
    public bool NeedsApproval { get; }

    /// <summary>本条是被项目级同名配置顶掉的那个全局 server（界面灰显）</summary>
    public bool IsShadowed { get; }

    /// 被覆盖的那条灰显。直接给数值而不是新造一个布尔转换器:整个项目还没有第二处要它
    public double Opacity => IsShadowed ? 0.45 : 1.0;

    /// <summary>
    /// 这一条预计会占的 token；<b>不会挂上的一律为 0</b>。
    ///
    /// 被覆盖的、没托管的、被角色禁的、待确认的都不该进合计——它们不会出现在下一轮的请求里，
    /// 把它们算进去会让那个数虚高，而这一栏的用途正是「该关掉哪个」。
    /// 从未连过的也为 0：那是「不知道」，不是 0，但拿不到就只能不计（面板上那条会显示状态而没有数）。
    /// </summary>
    public int ForecastTokens { get; }

    public McpPlannedServerItem(McpPlannedServer server)
    {
        Name = server.Name;
        CommandLine = server.CommandLine;
        NeedsApproval = server.NeedsApproval;
        IsShadowed = server.IsShadowed;
        ForecastTokens = server.WillBeMounted ? server.EstimatedTokens ?? 0 : 0;
        OriginText = server.IsWorkspaceScoped
            ? string.Format(LocalizationManager.Instance.GetString("AgentCapabilityMcpFromProject"),
                Path.GetFileName(server.WorkspacePath!.TrimEnd(Path.DirectorySeparatorChar)))
            : LocalizationManager.Instance.GetString("AgentCapabilityMcpFromGlobal");
        StatusText = DescribeStatus(server);
    }

    /// <summary>
    /// 一行话说清「这一条现在算什么」。次序即优先级——一条 server 可能同时被覆盖又没授权，
    /// 而用户第一步该处理的只有一个，先报最靠前那个原因。
    /// </summary>
    private static string DescribeStatus(McpPlannedServer server)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (server.IsShadowed) return loc.GetString("AgentCapabilityMcpShadowed");
        if (server.NeedsApproval) return loc.GetString("AgentCapabilityMcpNeedsApproval");
        if (server.IsHostingOff) return loc.GetString("AgentCapabilityMcpHostingOff");
        if (server.IsDisabledByCharacter) return loc.GetString("AgentCapabilityMcpDisabledByCharacter");

        // 连过又断开的报「上次的账」而不是 0:回收之后显示 0 个工具会被读成"这个 server 坏了"
        if (server.State != EMcpConnectionState.Connected && server.LastToolCount.HasValue)
        {
            return string.Format(loc.GetString("AgentCapabilityMcpLastSeen"),
                server.State, server.LastToolCount.Value, server.EstimatedTokens ?? 0);
        }

        return server.LastToolCount.HasValue
            ? $"{server.State} · {server.LastToolCount.Value} tools · ~{server.EstimatedTokens ?? 0} tok"
            : server.State.ToString();
    }
}
