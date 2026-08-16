/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools.Scheduler;
using UiharuMind.Shared.Services;
using UiharuMind.Features.Conversation.Pages;

namespace UiharuMind.Features.Conversation.SessionList;

/// <summary>
/// 智能体页右栏的定时任务列表：任务条目、取消 / 立即执行 / 打开结果会话。
/// 与会话列表无关，只是恰好长在同一个侧栏里——原先两者挤在 <c>AgentPageData</c> 里。
/// </summary>
public partial class ScheduledTaskListModel : ObservableObject, IDisposable
{
    private readonly Action<Action> _post;
    private readonly Action<string>? _openSession;

    public ObservableCollection<ScheduledTaskDisplayItem> Tasks { get; } = new();

    /// <param name="openSession">
    /// 「打开结果会话」的落点。会话列表归页面壳管，这里只把会话标识交出去
    /// </param>
    /// <param name="post">回 UI 线程的方式；测试传同步执行</param>
    public ScheduledTaskListModel(Action<string>? openSession = null, Action<Action>? post = null)
    {
        _openSession = openSession;
        _post = post ?? (action => Dispatcher.UIThread.Post(action));

        Sync();
        // 任务状态由后台计时器推进,条目是界面绑定的
        CharacterRunnerFactory.Instance.Scheduler.OnTaskUpdated += OnTaskUpdated;
    }

    /// <summary>
    /// 把列表对到调度器现在的样子
    /// </summary>
    public void Sync()
    {
        Tasks.Clear();
        foreach (ScheduledAgentTask task in CharacterRunnerFactory.Instance.Scheduler.GetTasks())
        {
            Tasks.Add(new ScheduledTaskDisplayItem(task));
        }
    }

    public void Dispose()
    {
        CharacterRunnerFactory.Instance.Scheduler.OnTaskUpdated -= OnTaskUpdated;
    }

    private void OnTaskUpdated(ScheduledAgentTask task) => _post(Sync);

    [RelayCommand]
    private async Task CancelTask(ScheduledTaskDisplayItem item)
    {
        await CharacterRunnerFactory.Instance.Scheduler.CancelAsync(item.TaskId);
    }

    [RelayCommand]
    private async Task RunTaskNow(ScheduledTaskDisplayItem item)
    {
        await CharacterRunnerFactory.Instance.Scheduler.RunNowAsync(item.TaskId);
    }

    [RelayCommand]
    private void OpenTaskSession(ScheduledTaskDisplayItem item)
    {
        if (string.IsNullOrEmpty(item.ResultSessionId)) return;
        _openSession?.Invoke(item.ResultSessionId);
    }
}

/// <summary>
/// 定时任务侧栏显示项
/// </summary>
public class ScheduledTaskDisplayItem
{
    /// <summary>任务标识</summary>
    public string TaskId { get; }

    /// <summary>显示名</summary>
    public string DisplayName { get; }

    /// <summary>触发时间文本</summary>
    public string FireAtText { get; }

    /// <summary>状态文本</summary>
    public string StatusText { get; }

    /// <summary>结果会话标识</summary>
    public string? ResultSessionId { get; }

    /// <summary>是否可取消(待触发/已错过)</summary>
    public bool CanCancel { get; }

    /// <summary>是否可立即执行(待触发/已错过/失败)</summary>
    public bool CanRunNow { get; }

    /// <summary>是否有结果会话可打开</summary>
    public bool HasResultSession { get; }

    public ScheduledTaskDisplayItem(ScheduledAgentTask task)
    {
        TaskId = task.TaskId;
        DisplayName = task.DisplayName;
        FireAtText = task.FireAt.ToString("MM-dd HH:mm");
        StatusText = LocalizationManager.Instance.GetString($"AgentTaskStatus{task.Status}");
        ResultSessionId = task.ResultSessionId;
        CanCancel = task.Status is EScheduledTaskStatus.Pending or EScheduledTaskStatus.Missed;
        CanRunNow = task.Status is EScheduledTaskStatus.Pending
            or EScheduledTaskStatus.Missed
            or EScheduledTaskStatus.Failed;
        HasResultSession = !string.IsNullOrEmpty(task.ResultSessionId);
    }
}
