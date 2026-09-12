/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Features.Conversation.SidePanels;

/// <summary>
/// 右栏「子代理」面板：本会话总共派过哪些活、哪个还没收尾。
///
/// 存在的理由是<b>入口</b>：子会话不进左栏（那是跨会话导航），而工具卡片在一个跑了几十轮的
/// 会话里早就滚没了。两个入口各说一件事——卡片说「它在上下文里的位置」，这里说「全会话索引」，
/// 点进去是同一个动作（<see cref="SubSessionWindowOpener"/>）。
///
/// 数据不需要新存储：子会话本来就在会话索引里，按 <c>ParentSessionId</c> 过滤即得。
/// </summary>
public partial class SubAgentListViewData : ObservableObject, IDisposable
{
    private readonly Func<string?> _parentSessionIdSource;

    /// <summary>本会话派出去的子会话，按最后更新时间倒序</summary>
    public ObservableCollection<SubSessionDisplayItem> Items { get; } = new();

    /// <param name="parentSessionIdSource">当前展示会话的标识来源（窄依赖，不反向持有主壳）</param>
    public SubAgentListViewData(Func<string?> parentSessionIdSource)
    {
        _parentSessionIdSource = parentSessionIdSource;
        SessionManager.Instance.Running.StateChanged += OnRunStateChanged;
    }

    public void Dispose()
    {
        SessionManager.Instance.Running.StateChanged -= OnRunStateChanged;
    }

    /// <summary>
    /// 重新读一遍。切会话时由页面壳调用；派活与收工靠运行态登记处自己触发
    /// </summary>
    public void Refresh()
    {
        Items.Clear();
        List<ChatSessionMeta> subs = SessionManager.Instance.GetSubSessions(_parentSessionIdSource());
        foreach (ChatSessionMeta meta in subs) Items.Add(new SubSessionDisplayItem(meta));
        OnPropertyChanged(nameof(Items));
    }

    /// <summary>
    /// 运行态变了。<b>可能来自后台线程</b>（子代理不在 UI 线程上），marshal 之后再动集合。
    /// 一次委派开始时会话刚进索引，所以这里是整体重读而不是只更新状态点
    /// </summary>
    private void OnRunStateChanged(string sessionId) => Dispatcher.UIThread.Post(Refresh);
}

/// <summary>
/// 面板里的一项：一次委派。
/// </summary>
public sealed class SubSessionDisplayItem
{
    private readonly ChatSessionMeta _meta;

    /// <param name="meta">子会话元数据</param>
    public SubSessionDisplayItem(ChatSessionMeta meta)
    {
        _meta = meta;
        Open = new RelayCommand(() => SubSessionWindowOpener.Open(_meta.SessionId));
    }

    /// <summary>任务摘要（子会话标题）</summary>
    public string Title => _meta.Title;

    /// <summary>被点名的子智能体名；匿名时为空</summary>
    public string AgentName => _meta.SubAgentName;

    /// <summary>有没有点名（匿名的那些不显示名字行）</summary>
    public bool HasAgentName => _meta.SubAgentName.Length > 0;

    /// <summary>最后更新时间</summary>
    public string TimeString => _meta.UpdatedAt.ToLocalTime().ToString("MM/dd HH:mm");

    /// <summary>是否仍在跑（含卡在审批上）</summary>
    public bool IsRunning => SessionManager.Instance.Running.IsBusy(_meta.SessionId);

    /// <summary>状态点配色键（status-dot 样式按 Tag 选色）</summary>
    public string StatusKey => IsRunning ? "Ready" : "Idle";

    /// <summary>打开这次委派的子会话</summary>
    public RelayCommand Open { get; }
}
