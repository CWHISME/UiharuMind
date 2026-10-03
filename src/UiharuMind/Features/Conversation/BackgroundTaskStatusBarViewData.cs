/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 输入区上方的一行后台任务
/// </summary>
/// <param name="Id">任务编号</param>
/// <param name="Description">一句话说明</param>
/// <param name="Command">命令原文（悬停提示）</param>
public sealed record BackgroundTaskStatusViewData(string Id, string Description, string Command);

/// <summary>
/// 输入区上方的<b>后台任务</b>：每个在跑的任务一行，带停止。
/// 与子代理状态行分开：那几行点一下是打开子会话，这里要的是叫停——模型那边没有停止工具，叫停归用户
/// </summary>
public sealed partial class BackgroundTaskStatusBarViewData
{
    /// <summary>在跑的任务，一个一行</summary>
    public ObservableCollection<BackgroundTaskStatusViewData> Items { get; } = new();

    /// <summary>
    /// 按会话重取在跑的任务（群窗口含成员起的）。只在 UI 线程上调
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public void Refresh(string? sessionId)
    {
        BackgroundTaskStatusViewData[] fresh = PendingWork.TasksOf(sessionId)
            .OrderBy(x => x.StartedAt)
            .Select(x => new BackgroundTaskStatusViewData(x.Id, x.Description, x.Command))
            .ToArray();
        //一字未变就别动集合,免得那几行跟着闪
        if (fresh.SequenceEqual(Items)) return;

        Items.Clear();
        foreach (BackgroundTaskStatusViewData task in fresh) Items.Add(task);
    }

    /// <summary>
    /// 叫停这个任务。结果照常送回会话（写明是被叫停的），行随之消失
    /// </summary>
    /// <param name="task">被点的那一行</param>
    [RelayCommand]
    private void Stop(BackgroundTaskStatusViewData? task)
    {
        if (task != null) BackgroundTaskRegistry.Stop(task.Id);
    }
}
