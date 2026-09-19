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
using System.Linq;
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
    private readonly DispatcherTimer _tickTimer;

    /// <summary>本会话派出去的子会话，运行中置顶、组内按本轮开始倒序</summary>
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

        // 秒表只对「在跑」的条目有意义：只在有运行时才开，tick 只刷文本不重建集合。
        // 数据本体在落盘的 LastRunStartedAt 上，timer 停了数字不跳而已，切回来重算即恢复
        _tickTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _tickTimer.Tick += (_, _) =>
        {
            foreach (SubSessionDisplayItem item in Items)
            {
                if (item.IsRunning) item.RefreshRunTime();
            }
        };
    }

    public void Dispose()
    {
        _tickTimer.Stop();
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
        foreach (ChatSessionMeta meta in OrderItems(subs))
        {
            SubSessionDisplayItem item = new(meta);
            if (item.IsRunning) running++;
            Items.Add(item);
        }

        RunningCount = running;
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(HasRunning));

        // 计时器跟着「有没有在跑」走：没有就不空转
        if (HasRunning) _tickTimer.Start();
        else _tickTimer.Stop();
    }

    /// <summary>
    /// 运行态变了。<b>可能来自后台线程</b>（子代理不在 UI 线程上），marshal 之后再动集合。
    /// 一次委派开始时会话刚进索引，所以这里是整体重读而不是只更新状态点
    /// </summary>
    private void OnRunStateChanged(string sessionId) => Dispatcher.UIThread.Post(Refresh);

    /// <summary>
    /// 排序：<b>运行中置顶，组内按本轮开始倒序（最新派发在最上）；已完成按结束时刻倒序。</b>
    ///
    /// 不用 UpdatedAt 当单一排序键：运行中每次落盘它都变，状态一变就重排，顺序来回跳看着就是无序的。
    /// 抽成静态便于测试。
    /// </summary>
    public static List<ChatSessionMeta> OrderItems(IEnumerable<ChatSessionMeta> subs)
    {
        List<ChatSessionMeta> all = [.. subs];
        List<ChatSessionMeta> running = all.Where(SubSessionDisplayItem.IsRunningMeta)
            .OrderByDescending(x => x.LastRunStartedAt ?? x.CreatedAt).ToList();
        List<ChatSessionMeta> done = all.Where(x => !SubSessionDisplayItem.IsRunningMeta(x))
            .OrderByDescending(x => x.UpdatedAt).ToList();
        running.AddRange(done);
        return running;
    }
}

/// <summary>
/// 面板里的一项：一次委派。
///
/// 一个子会话可能被续跑多次（<c>ContinueAsync</c> 复用同一会话），所以「运行时间」指的是
/// <b>最近一轮</b>：运行中显示「本次已运行」，跑完显示「末轮耗时」——而不是把中间所有空档
/// 都算进去的墙钟跨度。旧数据（无 <c>LastRunStartedAt</c>）回退显示最后更新时间戳。
/// </summary>
public sealed partial class SubSessionDisplayItem : ObservableObject
{
    private readonly ChatSessionMeta _meta;

    /// <param name="meta">子会话元数据</param>
    public SubSessionDisplayItem(ChatSessionMeta meta)
    {
        _meta = meta;
        Open = new RelayCommand(() => SubSessionWindowOpener.Open(_meta.SessionId));
        CopyId = new RelayCommand(() => App.Clipboard.CopyToClipboard(_meta.SessionId, true, true));
    }

    /// <summary>任务摘要（子会话标题）</summary>
    public string Title => _meta.Title;

    /// <summary>被点名的子智能体名；匿名时为空</summary>
    public string AgentName => _meta.SubAgentName;

    /// <summary>有没有点名（匿名的那些不显示名字行）</summary>
    public bool HasAgentName => _meta.SubAgentName.Length > 0;

    /// <summary>
    /// 是否仍在跑（含卡在审批上）。<b>兼看后台标记</b>：报告还没交回就算没完，
    /// 哪怕那一瞬间执行者正好闲着（两次服务调用之间）
    /// </summary>
    public bool IsRunning => IsRunningMeta(_meta);

    /// <summary>
    /// 跑得太久了。<c>SubAgentTool.Timeout</c>（24 小时）刻意没动，靠这一档把它顶到用户眼前。
    /// 判据用<b>本轮起点</b>而不是创建时刻：续跑多次的会话创建很早，用创建时刻会把
    /// 「本轮刚开」的续跑误标成跑了很久（见 ADR 0025 的同一背景）
    /// </summary>
    public bool IsLongRunning => IsRunning
                                 && DateTimeOffset.Now - RunStart > BackgroundSubAgentDispatcher.LongRunNotice;

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

    /// <summary>本轮起点：新字段优先，旧数据回退创建时刻</summary>
    private DateTimeOffset RunStart => _meta.LastRunStartedAt ?? _meta.CreatedAt;

    /// <summary>
    /// 时间列文本：在跑 = 「本次」秒表；跑完且有本轮起点 = 「末轮」耗时；
    /// 旧数据（没有本轮起点） = 最后更新时间戳，不硬算墙钟跨度去骗人
    /// </summary>
    public string RunTimeText
    {
        get
        {
            if (IsRunning) return $"本次 {FormatDuration(DateTimeOffset.Now - RunStart)}";
            if (_meta.LastRunStartedAt is { } started)
            {
                return $"末轮 {FormatDuration(_meta.UpdatedAt - started)}";
            }
            return _meta.UpdatedAt.ToLocalTime().ToString("MM/dd HH:mm");
        }
    }

    /// <summary>时间列悬停提示：完整起止时刻。原「最后更新时间」挪到这里，信息不丢</summary>
    public string RunTimeTip
    {
        get
        {
            string start = RunStart.ToLocalTime().ToString("MM/dd HH:mm:ss");
            return IsRunning
                ? $"开始 {start} · 进行中"
                : $"开始 {start} · 结束 {_meta.UpdatedAt.ToLocalTime():MM/dd HH:mm:ss}";
        }
    }

    /// <summary>秒表 tick 用：只刷这两条文本，集合与其余属性不动</summary>
    public void RefreshRunTime()
    {
        OnPropertyChanged(nameof(RunTimeText));
        OnPropertyChanged(nameof(RunTimeTip));
    }

    /// <summary>判断一次委派是否仍在跑。挂在条目上供列表排序共用，避免两份实现漂移</summary>
    public static bool IsRunningMeta(ChatSessionMeta meta) =>
        SessionManager.Instance.Running.IsBusy(meta.SessionId) || meta.BackgroundReportPending;

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
