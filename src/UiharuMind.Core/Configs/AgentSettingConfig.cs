/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs;

/// <summary>
/// Agent 工作区的<b>全局</b>标量配置：最近工作目录、搜索 API 凭据、各页受管参数。
///
/// 工具开关与技能禁用清单长在角色身上(<see cref="UiharuMind.Core.AI.Character.AgentToolConfig"/>),
/// 运行时只读那一份,没有全局总闸(见 ADR 0003)。<see cref="ModelSkillsEnabled"/> 是这条规则的
/// <b>唯一例外</b>:它是"模型看不看得到技能"的运行时呈现偏好,不是"这个角色有什么能力"——
/// 技能目录是全局事实,模型可见性是个人偏好,按角色下沉反而错。修订见 ADR 0003 附记。
/// </summary>
public class AgentSettingConfig : TConfigBase<AgentSettingConfig>
{
    // 出厂值（设置页「单项恢复默认」用）：与各属性初始值同源，页面 reset 命令引用这里而不是手抄数字
    public const int FactoryDefaultPermissionModeIndex = 1;
    public const int FactoryDefaultAwayMaxHours = 24;
    public const int FactoryDefaultAwayMaxAvatarTurns = 500;
    public const int FactoryDefaultAwayMaxIdleWaves = 50;
    public const int FactoryDefaultAwayBackoffStartSeconds = 60;
    public const int FactoryDefaultAwayBackoffMaxMinutes = 30;
    public const int FactoryDefaultAwayStopDelayMinutes = 5;
    public const string FactoryDefaultPythonInterpreterPath = "";
    public const string FactoryDefaultFirecrawlApiKey = "";
    public const string FactoryDefaultTavilyApiKey = "";
    public const string FactoryDefaultBraveSearchApiKey = "";

    /// <summary>新会话默认权限档(0 只读 / 1 自动编辑 / 2 完全自动)</summary>
    public int DefaultPermissionModeIndex { get; set; } = FactoryDefaultPermissionModeIndex;

    /// <summary>
    /// 是否把技能清单与 load_skill 工具集发给模型。关掉后模型侧看不到技能广告列表,
    /// 框架的 load_skill 等三个工具不挂,只剩点名调用可达,外加自建同名 load_skill
    /// 用于加载点名技能正文里引用的被动技能(见 LoadSkillTool)——给本地小窗口模型
    /// 腾固定开销用的。「没有全局总闸」是 ADR 0003 的决策,本条是它的唯一例外,理由见类注释。
    /// </summary>
    public bool ModelSkillsEnabled { get; set; }

    /// <summary>
    /// 最近用过的工作目录(最新在前)。切换工作区是高频操作，每次都重新翻文件选择器太笨。
    /// 刻意不从会话记录反推：那样删掉会话就等于失忆，也没法单独移除某一条。
    /// </summary>
    public List<string> RecentWorkspaces { get; set; } = new();

    /// <summary>
    /// Firecrawl API key。<b>可以不填</b>——Firecrawl 无 key 也能用(按 IP 限额),它是搜索与
    /// 正文抓取两条兜底链的首选;填了只是把额度换成账号维度的。
    /// </summary>
    public string FirecrawlApiKey { get; set; } = FactoryDefaultFirecrawlApiKey;

    /// <summary>Tavily 搜索 API key(填入后搜索优先走正规 API,空则用爬页面兜底链)</summary>
    public string TavilyApiKey { get; set; } = FactoryDefaultTavilyApiKey;

    /// <summary>Brave Search API key(同上,优先级次于 Tavily)</summary>
    public string BraveSearchApiKey { get; set; } = FactoryDefaultBraveSearchApiKey;

    /// <summary>
    /// 宿主 Python 解释器路径。<b>只用于创建受管虚拟环境那一次</b>,建完之后 agent 用的
    /// 一直是虚拟环境里那个,与这里填的是谁无关。
    ///
    /// 空 = 自动探测(PATH 上的 python3/python)。填它是为了自动探测不中的情况:
    /// 探测只认 PATH,而 Windows 上装了 Python 却没勾"Add to PATH"是常态。
    /// </summary>
    public string PythonInterpreterPath { get; set; } = FactoryDefaultPythonInterpreterPath;

    /// <summary>
    /// 探索型子代理使用的模型名(空 = 回退到主代理模型)。
    ///
    /// ⚠️ <b>仅为重建存量</b>（ADR 0044 阶段 2「就地封存」）：新的委派一律走通用档，
    /// 这个值只在续跑<b>老的</b>只读子会话时还被读到。设置页那个选择器已经删掉——
    /// 一个对新委派不起作用的控件留着只会误导。字段本身不删：删了存量重建就取不到模型。
    /// </summary>
    public string ExplorerSubAgentModelName { get; set; } = string.Empty;

    /// <summary>
    /// 通用子代理使用的模型名(空 = 回退到主代理模型)。
    /// 通用子代理做实际修改操作,可给它配一个与主代理不同的模型
    /// (比如主代理用推理慢的大模型,通用子代理用快模型)。
    /// </summary>
    public string GeneralSubAgentModelName { get; set; } = string.Empty;

    /// <summary><see cref="RecentWorkspaces"/> 的条数上限</summary>
    public const int RecentWorkspacesLimit = 10;

    private RecentPathList? _recentWorkspaces;
    private RecentPathList RecentHistory =>
        _recentWorkspaces ??= new RecentPathList(RecentWorkspaces, RecentWorkspacesLimit);

    /// <summary>离席最长多少小时（保险丝，只防失控，ADR 0055）</summary>
    public int AwayMaxHours { get; set; } = FactoryDefaultAwayMaxHours;

    /// <summary>一次离席里化身最多出手几次（保险丝）</summary>
    public int AwayMaxAvatarTurns { get; set; } = FactoryDefaultAwayMaxAvatarTurns;

    /// <summary>连续多少波没有新产物就结束离席（保险丝）</summary>
    public int AwayMaxIdleWaves { get; set; } = FactoryDefaultAwayMaxIdleWaves;

    /// <summary>化身没进展时，第一次延迟唤醒等多少秒；之后逐次翻倍</summary>
    public int AwayBackoffStartSeconds { get; set; } = FactoryDefaultAwayBackoffStartSeconds;

    /// <summary>没进展时延迟唤醒的封顶分钟数</summary>
    public int AwayBackoffMaxMinutes { get; set; } = FactoryDefaultAwayBackoffMaxMinutes;

    /// <summary>离席中用户按了停止（没关离席）后，等多少分钟再唤醒化身</summary>
    public int AwayStopDelayMinutes { get; set; } = FactoryDefaultAwayStopDelayMinutes;

    /// <summary>
    /// 把一个工作目录记为最近使用:置顶、去重、裁尾,并立即落盘。列表操作见 <see cref="RecentPathList"/>。
    /// </summary>
    /// <param name="path">工作目录;空或不存在则忽略</param>
    public void RememberWorkspace(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        if (RecentHistory.Remember(path)) Save();
    }

    /// <summary>
    /// 把一个工作目录从最近列表里移除(用户主动剔除,或目录已经不在了)并落盘。
    /// </summary>
    /// <param name="path">工作目录</param>
    public void ForgetWorkspace(string? path)
    {
        if (RecentHistory.Forget(path)) Save();
    }
}
