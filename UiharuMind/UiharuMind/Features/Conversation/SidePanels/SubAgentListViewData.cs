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
using UiharuMind.Core.AI.Execution.Tools;

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

    /// <summary>
    /// 此刻还在跑的条数。与 <c>Items.Count</c> 分开说：后者是「总共派过多少活」（只增不减），
    /// 而用户要知道的是「现在还有几件没完」。委派默认后台执行之后这个数常态大于 1
    /// </summary>
    public int RunningCount { get; private set; }

    /// <summary>还有没有在跑的（面板标题据此显示计数）</summary>
    public bool HasRunning => RunningCount > 0;

    /// <param name="parentSessionIdSource">当前展示会话的标识来源（窄依赖，不反向持有主壳）</param>
    public SubAgentListViewData(Func<string?> parentSessionIdSource)
    {
        _parentSessionIdSource = parentSessionIdSource;
        SessionManager.Instance.Running.StateChanged += OnRunStateChanged;
        BackgroundSubAgentDispatcher.PendingWorkChanged += OnRunStateChanged;
    }

    public void Dispose()
    {
        SessionManager.Instance.Running.StateChanged -= OnRunStateChanged;
        BackgroundSubAgentDispatcher.PendingWorkChanged -= OnRunStateChanged;
    }

    /// <summary>
    /// 重新读一遍。切会话时由页面壳调用；派活与收工靠运行态登记处自己触发
    /// </summary>
    public void Refresh()
    {
        Items.Clear();
        List<ChatSessionMeta> subs = SessionManager.Instance.GetSubSessions(_parentSessionIdSource());
        int running = 0;
        foreach (ChatSessionMeta meta in subs)
        {
            SubSessionDisplayItem item = new(meta);
            if (item.IsRunning) running++;
            Items.Add(item);
        }

        RunningCount = running;
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(HasRunning));
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
        CopyId = new RelayCommand(() => App.Clipboard.CopyToClipboard(_meta.SessionId, true));
    }

    /// <summary>任务摘要（子会话标题）</summary>
    public string Title => _meta.Title;

    /// <summary>被点名的子智能体名；匿名时为空</summary>
    public string AgentName => _meta.SubAgentName;

    /// <summary>有没有点名（匿名的那些不显示名字行）</summary>
    public bool HasAgentName => _meta.SubAgentName.Length > 0;

    /// <summary>最后更新时间</summary>
    public string TimeString => _meta.UpdatedAt.ToLocalTime().ToString("MM/dd HH:mm");

    /// <summary>
    /// 是否仍在跑（含卡在审批上）。<b>兼看后台标记</b>：报告还没交回就算没完，
    /// 哪怕那一瞬间执行者正好闲着（两次服务调用之间）
    /// </summary>
    public bool IsRunning => SessionManager.Instance.Running.IsBusy(_meta.SessionId)
                             || _meta.BackgroundReportPending;

    /// <summary>
    /// 跑得太久了。<c>SubAgentTool.Timeout</c>（24 小时）刻意没动，靠这一档把它顶到用户眼前
    /// ——后台化之后「有人看着」这个前提削弱了，但改成「按有没有人看着定上限」会让同一次委派
    /// 因为用户开没开窗口而结局不同（见 ADR 0025）
    /// </summary>
    public bool IsLongRunning => IsRunning
                                 && DateTimeOffset.Now - _meta.CreatedAt > BackgroundSubAgentDispatcher.LongRunNotice;

    /// <summary>
    /// 卡在审批上等人点选。<b>与「在跑」分开一档</b>：它是唯一需要用户动手的状态，
    /// 而且有时限（<c>SubAgentTool.NestedApprovalTimeout</c> 到期按拒绝收口，那次委派基本白跑）。
    /// 共用一个绿点的话，用户没有任何理由点进去看
    /// </summary>
    public bool IsAwaitingApproval =>
        SessionManager.Instance.Running.StateOf(_meta.SessionId) == ESessionRunState.AwaitingApproval;

    /// <summary>
    /// 状态点配色键（status-dot 样式按 Tag 选色）。
    /// 优先级即紧迫度：要你动手 &gt; 跑太久 &gt; 在跑 &gt; 空
    /// </summary>
    public string StatusKey => IsAwaitingApproval ? "Warning"
        : IsLongRunning ? "Progress"
        : IsRunning ? "Ready"
        : "Idle";

    /// <summary>打开这次委派的子会话</summary>
    public RelayCommand Open { get; }

    /// <summary>
    /// 复制这次委派的编号。
    ///
    /// 存在的理由：`ContinueSubAgent` 认的是编号，而用户想点名让主代理续跑某一次委派时，
    /// 编号在界面上本来只出现在工具卡的结果里——那张卡跑几十轮就滚没了。
    /// </summary>
    public RelayCommand CopyId { get; }
}
