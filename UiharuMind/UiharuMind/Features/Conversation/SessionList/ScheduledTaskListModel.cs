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
    private readonly ISchedulerBackend _scheduler;
    private readonly Action<Action> _post;
    private readonly Action<string>? _openSession;

    public ObservableCollection<ScheduledTaskDisplayItem> Tasks { get; } = new();

    /// <param name="openSession">
    /// 「打开结果会话」的落点。会话列表归页面壳管，这里只把会话标识交出去
    /// </param>
    /// <param name="post">回 UI 线程的方式；测试传同步执行</param>
    /// <param name="scheduler">调度后端；不传取生产单例那一个（测试传替身，因此默认值只在为 null 时求值）</param>
    public ScheduledTaskListModel(Action<string>? openSession = null, Action<Action>? post = null,
        ISchedulerBackend? scheduler = null)
    {
        _openSession = openSession;
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        _scheduler = scheduler ?? CharacterRunnerFactory.Instance.Scheduler;

        Sync();
        // 任务状态由后台计时器推进,条目是界面绑定的
        _scheduler.OnTaskUpdated += OnTaskUpdated;
    }

    /// <summary>
    /// 把列表对到调度器现在的样子
    /// </summary>
    public void Sync()
    {
        Tasks.Clear();
        foreach (ScheduledAgentTask task in _scheduler.GetTasks())
        {
            Tasks.Add(new ScheduledTaskDisplayItem(task, ApplyPermissionMode));
        }
    }

    public void Dispose()
    {
        _scheduler.OnTaskUpdated -= OnTaskUpdated;
    }

    private void OnTaskUpdated(ScheduledAgentTask task) => _post(Sync);

    /// <summary>
    /// 条目上的三档选择改动落到后端。不做成命令:ComboBox 的 SelectedIndex 是双向绑定,
    /// 没有命令可挂。后端落盘后会回一次 OnTaskUpdated,列表随之重建,不会递归
    /// (重建出来的条目直接写字段,不再回调)。
    /// </summary>
    private void ApplyPermissionMode(string taskId, int index)
    {
        _ = _scheduler.SetPermissionModeAsync(taskId, (EAgentPermissionMode)Math.Clamp(index, 0, 2));
    }

    [RelayCommand]
    private async Task CancelTask(ScheduledTaskDisplayItem item)
    {
        await _scheduler.CancelAsync(item.TaskId);
    }

    [RelayCommand]
    private async Task RunTaskNow(ScheduledTaskDisplayItem item)
    {
        await _scheduler.RunNowAsync(item.TaskId);
    }

    [RelayCommand]
    private void OpenTaskSession(ScheduledTaskDisplayItem item)
    {
        if (string.IsNullOrEmpty(item.ResultSessionId)) return;
        _openSession?.Invoke(item.ResultSessionId);
    }
}

/// <summary>
/// 定时任务侧栏显示项。权限档是可改的那一项(仅未执行的任务)，其余皆为只读投影。
/// </summary>
public partial class ScheduledTaskDisplayItem : ObservableObject
{
    private readonly Action<string, int>? _applyPermissionMode;

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

    /// <summary>权限档是否还能改(跑过之后改了只会让显示与实际跑过的那一档不符)</summary>
    public bool CanEditPermission { get; }

    /// <summary>权限档显示名(与对话页顶栏同一套文案)</summary>
    public string PermissionText => ConversationModeLabels.PermissionLabel(PermissionModeIndex);

    /// <summary>无人值守执行用的权限档序号(EAgentPermissionMode)</summary>
    [ObservableProperty] private int _permissionModeIndex;

    /// <param name="task">任务</param>
    /// <param name="applyPermissionMode">权限档改动的落点(任务标识, 档位序号)；只读展示可不传</param>
    public ScheduledTaskDisplayItem(ScheduledAgentTask task, Action<string, int>? applyPermissionMode = null)
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
        CanEditPermission = task.Status is EScheduledTaskStatus.Pending or EScheduledTaskStatus.Missed;
        // 先写字段再挂回调:走属性会立刻回调一次,把"重建列表"变成一次多余的写盘
        _permissionModeIndex = (int)task.PermissionMode;
        _applyPermissionMode = applyPermissionMode;
    }

    partial void OnPermissionModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(PermissionText));
        _applyPermissionMode?.Invoke(TaskId, value);
    }
}
