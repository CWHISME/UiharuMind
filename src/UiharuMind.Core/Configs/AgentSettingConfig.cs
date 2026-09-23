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
/// Agent 工作区的<b>全局</b>标量配置：新会话默认值、最近工作目录、搜索 API 凭据。
///
/// 工具开关与技能禁用清单长在角色身上(<see cref="UiharuMind.Core.AI.Character.AgentToolConfig"/>),
/// 运行时只读那一份,没有全局总闸(见 ADR 0003)。<see cref="ModelSkillsEnabled"/> 是这条规则的
/// <b>唯一例外</b>:它是"模型看不看得到技能"的运行时呈现偏好,不是"这个角色有什么能力"——
/// 技能目录是全局事实,模型可见性是个人偏好,按角色下沉反而错。修订见 ADR 0003 附记。
/// </summary>
public class AgentSettingConfig : TConfigBase<AgentSettingConfig>
{
    /// <summary>新会话默认权限档(0 只读 / 1 自动编辑 / 2 完全自动)</summary>
    public int DefaultPermissionModeIndex { get; set; } = 1;

    /// <summary>新会话默认工作目录(空 = 不绑定)</summary>
    public string DefaultWorkspacePath { get; set; } = string.Empty;

    /// <summary>新会话默认开启 plan 模式</summary>
    public bool DefaultPlanMode { get; set; }

    /// <summary>
    /// 是否把技能清单与 load_skill 工具集发给模型。关掉后模型侧完全看不到技能,
    /// 只剩点名调用可达(正文直接注入)——给本地小窗口模型腾固定开销用的。
    /// 「没有全局总闸」是 ADR 0003 的决策,本条是它的唯一例外,理由见类注释。
    /// </summary>
    public bool ModelSkillsEnabled { get; set; } = true;

    /// <summary>
    /// 最近用过的工作目录(最新在前)。切换工作区是高频操作，每次都重新翻文件选择器太笨。
    /// 刻意不从会话记录反推：那样删掉会话就等于失忆，也没法单独移除某一条。
    /// </summary>
    public List<string> RecentWorkspaces { get; set; } = new();

    /// <summary>
    /// Firecrawl API key。<b>可以不填</b>——Firecrawl 无 key 也能用(按 IP 限额),它是搜索与
    /// 正文抓取两条兜底链的首选;填了只是把额度换成账号维度的。
    /// </summary>
    public string FirecrawlApiKey { get; set; } = string.Empty;

    /// <summary>Tavily 搜索 API key(填入后搜索优先走正规 API,空则用爬页面兜底链)</summary>
    public string TavilyApiKey { get; set; } = string.Empty;

    /// <summary>Brave Search API key(同上,优先级次于 Tavily)</summary>
    public string BraveSearchApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 宿主 Python 解释器路径。<b>只用于创建受管虚拟环境那一次</b>,建完之后 agent 用的
    /// 一直是虚拟环境里那个,与这里填的是谁无关。
    ///
    /// 空 = 自动探测(PATH 上的 python3/python)。填它是为了自动探测不中的情况:
    /// 探测只认 PATH,而 Windows 上装了 Python 却没勾"Add to PATH"是常态。
    /// </summary>
    public string PythonInterpreterPath { get; set; } = string.Empty;

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
