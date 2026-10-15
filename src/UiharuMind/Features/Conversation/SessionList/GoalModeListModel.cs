using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Single.GoalMode;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.SessionList;

/// <summary>
/// 智能体页右栏的目标模式清单：进行中的在前，最近结束的回执在后；停止 / 打开会话 / 清除已结束。
/// 与定时任务同一页签，但各管各的
/// </summary>
public sealed partial class GoalModeListModel : ObservableObject, IDisposable
{
    private readonly GoalModeController _goalMode;
    private readonly Action<Action> _post;
    private readonly Action<string>? _openSession;
    private readonly List<GoalModeReceipt> _receipts = []; //最近结束的回执（只在内存，可清除）

    /// <summary>构造</summary>
    /// <param name="openSession">「打开」的落点：会话列表归页面壳管，这里只把会话标识交出去</param>
    /// <param name="post">回 UI 线程的方式；测试传同步执行</param>
    /// <param name="goalMode">目标模式控制器；不传取应用里的那一个</param>
    public GoalModeListModel(Action<string>? openSession = null, Action<Action>? post = null,
        GoalModeController? goalMode = null)
    {
        _openSession = openSession;
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        _goalMode = goalMode ?? GoalModeController.Instance;
        _goalMode.StatusChanged += OnStatusChanged;
        _goalMode.Ended += OnEnded;
        Sync();
    }

    /// <summary>清单条目</summary>
    public ObservableCollection<GoalModeDisplayItem> Items { get; } = new();

    /// <summary>有没有已结束的回执（「清除已完成」按钮据此显隐）</summary>
    public bool HasDone
    {
        get
        {
            lock (_receipts) return _receipts.Count > 0;
        }
    }

    /// <summary>摘掉订阅</summary>
    public void Dispose()
    {
        _goalMode.StatusChanged -= OnStatusChanged;
        _goalMode.Ended -= OnEnded;
    }

    /// <summary>把清单对到目标模式现在的样子</summary>
    public void Sync()
    {
        Items.Clear();
        foreach (GoalModeStatus status in _goalMode.ActiveStatuses()) Items.Add(GoalModeDisplayItem.ForStatus(status));

        List<GoalModeReceipt> done;
        lock (_receipts) done = _receipts.ToList();
        foreach (GoalModeReceipt receipt in done) Items.Add(GoalModeDisplayItem.ForReceipt(receipt));

        OnPropertyChanged(nameof(HasDone));
    }

    private void OnStatusChanged(string _) => _post(Sync);

    private void OnEnded(GoalModeReceipt receipt)
    {
        lock (_receipts) _receipts.Add(receipt);
        _post(Sync);
    }

    [RelayCommand]
    private void Stop(GoalModeDisplayItem item) => _goalMode.End(item.SessionId);

    [RelayCommand]
    private void Open(GoalModeDisplayItem item) => _openSession?.Invoke(item.SessionId);

    [RelayCommand]
    private void ClearDone()
    {
        lock (_receipts) _receipts.Clear();
        Sync();
    }
}

/// <summary>
/// 目标模式清单的显示项：进行中的（会话名/目标/状态/停止）或最近结束的回执
/// </summary>
public sealed class GoalModeDisplayItem
{
    private GoalModeDisplayItem(string sessionId, string goal, string statusText, bool isActive)
    {
        SessionId = sessionId;
        Title = SessionManager.Instance.GetMeta(sessionId)?.Title ?? string.Empty;
        Goal = goal;
        StatusText = statusText;
        IsActive = isActive;
    }

    /// <summary>会话标识</summary>
    public string SessionId { get; }

    /// <summary>会话标题</summary>
    public string Title { get; }

    /// <summary>目标或回执总结（完整文本，界面用省略号收）</summary>
    public string Goal { get; }

    /// <summary>状态文本</summary>
    public string StatusText { get; }

    /// <summary>进行中（可停止）；否则是已结束的回执</summary>
    public bool IsActive { get; }

    /// <summary>进行中的条目</summary>
    /// <param name="status">状态</param>
    /// <returns>条目</returns>
    public static GoalModeDisplayItem ForStatus(GoalModeStatus status)
    {
        LangKey key = status.IsRunning ? LangKey.GoalModeThinking
            : status.WakeAt != null ? LangKey.GoalModeWaitingWake
            : LangKey.GoalModeWaitingManual;
        return new GoalModeDisplayItem(status.SessionId, status.Goal, Loc.Text(key), true);
    }

    /// <summary>结束回执的条目</summary>
    /// <param name="receipt">回执</param>
    /// <returns>条目</returns>
    public static GoalModeDisplayItem ForReceipt(GoalModeReceipt receipt)
    {
        LangKey key = receipt.Reason switch
        {
            EGoalModeEndReason.Done => LangKey.GoalModeEndedDone,
            EGoalModeEndReason.NeedsUser => LangKey.GoalModeEndedNeedsUser,
            _ => LangKey.GoalModeEndedManual,
        };
        return new GoalModeDisplayItem(receipt.SessionId, receipt.Summary, Loc.Text(key), false);
    }
}
