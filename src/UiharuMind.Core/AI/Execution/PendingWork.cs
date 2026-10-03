/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 会话名下<b>未了结的工作</b>（见 CONTEXT.md）：后台子代理还没交回，或者后台任务还在跑。
/// 它不是 IsGenerating——那一轮早结束了，这些只驱动指示器、拦住删除与卸载。
///
/// 问「还有没有活」一律问这里，不分别去问各个来源：再多一种后台工作也只改这一处。
/// 群壳名下算上成员起的任务：删群会级联删成员，群窗口也是用户唯一看得见它们的地方
/// </summary>
public static class PendingWork
{
    /// <summary>某个会话的未了结工作有增减（参数为会话标识，<b>可能在后台线程触发</b>）</summary>
    public static event Action<string>? Changed;

    static PendingWork()
    {
        BackgroundSubAgentDispatcher.PendingWorkChanged += Raise;
        BackgroundTaskRegistry.Changed += OnTasksChanged;
    }

    /// <summary>
    /// 这个会话名下有没有未了结的工作
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>有则 true</returns>
    public static bool Has(string? sessionId) =>
        BackgroundSubAgentDispatcher.HasPendingWork(sessionId)
        || (BackgroundTaskRegistry.AnyRunning() && TasksOf(sessionId).Count > 0); //先问有没有任务:指示器高频调,没任务时不分配

    /// <summary>
    /// 这个会话名下在跑的后台任务；群壳含成员起的
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>任务快照；没有则空</returns>
    public static IReadOnlyList<BackgroundTask> TasksOf(string? sessionId) =>
        string.IsNullOrEmpty(sessionId)
            ? []
            : BackgroundTaskRegistry.AllRunning()
                .Where(x => x.OwnerSessionId == sessionId || GroupOf(x.OwnerSessionId) == sessionId)
                .ToArray();

    /// <summary>进程内有没有<b>任何</b>未了结的工作（菜单栏图标那一档是全局的）</summary>
    public static bool Any() => BackgroundSubAgentDispatcher.AnyPending() || BackgroundTaskRegistry.AnyRunning();

    private static void OnTasksChanged(string ownerSessionId)
    {
        Raise(ownerSessionId);
        if (GroupOf(ownerSessionId) is { } groupId) Raise(groupId);
    }

    private static string? GroupOf(string sessionId) => SessionManager.Instance.GetMeta(sessionId)?.GroupId;

    private static void Raise(string sessionId) => Changed?.Invoke(sessionId);
}
