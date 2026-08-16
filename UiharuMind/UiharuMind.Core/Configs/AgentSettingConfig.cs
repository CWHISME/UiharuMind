/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.Configs;

/// <summary>
/// Agent 工作区的<b>全局</b>标量配置：新会话默认值、最近工作目录、搜索 API 凭据。
///
/// 刻意<b>不含</b>工具开关与技能禁用清单——那些是"这个智能体有什么能力"，长在角色身上
/// (<see cref="UiharuMind.Core.AI.Character.AgentToolConfig"/>)，运行时只读那一份，
/// 没有全局总闸(见 ADR 0003)。
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

    /// <summary><see cref="RecentWorkspaces"/> 的条数上限</summary>
    public const int RecentWorkspacesLimit = 10;

    /// <summary>
    /// 把一个工作目录记为最近使用:置顶、去重(按路径逐字比较)、裁掉超限的尾部,并立即落盘。
    /// </summary>
    /// <param name="path">工作目录;空或不存在则忽略</param>
    public void RememberWorkspace(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        string full = Path.GetFullPath(path);
        RecentWorkspaces.RemoveAll(x => string.Equals(x, full, StringComparison.Ordinal));
        RecentWorkspaces.Insert(0, full);
        if (RecentWorkspaces.Count > RecentWorkspacesLimit)
        {
            RecentWorkspaces.RemoveRange(RecentWorkspacesLimit,
                RecentWorkspaces.Count - RecentWorkspacesLimit);
        }

        Save();
    }

    /// <summary>
    /// 把一个工作目录从最近列表里移除(用户主动剔除,或目录已经不在了)并落盘。
    /// </summary>
    /// <param name="path">工作目录</param>
    public void ForgetWorkspace(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (RecentWorkspaces.RemoveAll(x => string.Equals(x, path, StringComparison.Ordinal)) > 0) Save();
    }
}
