/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

/// <summary>
/// 进程内所有在跑的后台任务。
///
/// 必须是静态的：工具实例随装配快照重建、执行者随会话释放，而任务要活过它们
/// （与 <see cref="BackgroundSubAgentDispatcher"/> 同理）
/// </summary>
public static class BackgroundTaskRegistry
{
    private static readonly object _lock = new(); //「结束→待送达」的转移与退出收尾互斥:同一个任务只由一边落结局
    private static readonly ConcurrentDictionary<string, Entry> _running = new();
    // 已结束、结果还没落进会话的(等会话空闲、等群里轮到他)。退出时它们也要落,否则结果就丢了
    private static readonly ConcurrentDictionary<string, Undelivered> _undelivered = new();
    private static bool _shuttingDown;

    /// <summary>某个会话名下的在跑任务有增减（参数为会话标识，可能在后台线程触发）</summary>
    public static event Action<string>? Changed;

    /// <summary>
    /// 起一个后台任务并登记，结束后交给 <paramref name="sink"/> 送达
    /// </summary>
    /// <param name="ownerSessionId">启动它的会话标识</param>
    /// <param name="command">命令原文</param>
    /// <param name="description">一句话说明</param>
    /// <param name="maxRuntime">时间上限</param>
    /// <param name="launch">起跑环境</param>
    /// <param name="sink">结果送到哪儿</param>
    /// <returns>在跑的任务</returns>
    /// <exception cref="InvalidOperationException">应用正在退出</exception>
    public static BackgroundTask Start(string ownerSessionId, string command, string description, TimeSpan maxRuntime,
        BackgroundTaskLaunch launch, IBackgroundTaskReportSink sink)
    {
        BackgroundTask task;
        lock (_lock)
        {
            // 退出收尾之后再起的没人收:它会活过应用,结果也无处可落
            if (_shuttingDown) throw new InvalidOperationException("the app is shutting down");
            task = BackgroundTask.Start(ownerSessionId, command, description, maxRuntime, launch);
            _running[task.Id] = new Entry(task, sink);
        }

        Changed?.Invoke(ownerSessionId);
        _ = WatchAsync(task, sink);
        return task;
    }

    /// <summary>
    /// 某个会话名下在跑的任务
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>任务快照；没有则空</returns>
    public static IReadOnlyList<BackgroundTask> RunningOf(string? sessionId) =>
        string.IsNullOrEmpty(sessionId)
            ? []
            : _running.Values.Select(x => x.Task).Where(x => x.OwnerSessionId == sessionId).ToArray();

    /// <summary>进程内所有在跑的任务（快照）</summary>
    /// <returns>任务快照</returns>
    public static IReadOnlyList<BackgroundTask> AllRunning() => _running.Values.Select(x => x.Task).ToArray();

    /// <summary>进程内有没有任何在跑的任务</summary>
    public static bool AnyRunning() => !_running.IsEmpty;

    /// <summary>
    /// 用户叫停一个任务：结束整棵进程树，结果照常送回（写明是被叫停的）
    /// </summary>
    /// <param name="taskId">任务编号</param>
    /// <returns>找到并叫停了则 true；已经结束的为 false</returns>
    public static bool Stop(string taskId)
    {
        if (!_running.TryGetValue(taskId, out Entry? entry)) return false;
        entry.Task.Kill(EBackgroundTaskEnd.Stopped);
        return true;
    }

    /// <summary>
    /// 叫停某个会话名下的全部任务（会话被删时）。结果送不回已删的会话，就此作罢
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public static void StopAllOf(string sessionId)
    {
        foreach (BackgroundTask task in RunningOf(sessionId)) task.Kill(EBackgroundTaskEnd.Stopped);
    }

    /// <summary>
    /// 应用退出：结束所有在跑的任务、落下所有还没送达的结果（不唤醒）。必须在轮次收尾之后调
    /// （见 <c>TurnDriver.SettleAllForShutdown</c>），此时没有轮次再写历史
    /// </summary>
    public static void SettleAllForShutdown()
    {
        Entry[] running;
        Undelivered[] undelivered;
        lock (_lock)
        {
            _shuttingDown = true;
            running = _running.Values.ToArray();
            undelivered = _undelivered.Values.ToArray();
            _running.Clear();
            _undelivered.Clear();
        }

        foreach (Entry entry in running)
        {
            entry.Task.Kill(EBackgroundTaskEnd.AppExit);
            DeliverOnShutdown(entry.Sink, new BackgroundTaskOutcome(entry.Task, EBackgroundTaskEnd.AppExit, null,
                DateTimeOffset.Now - entry.Task.StartedAt));
        }

        // 已经跑完的照它真实的结局落,不改成「被中止」
        foreach (Undelivered pending in undelivered) DeliverOnShutdown(pending.Sink, pending.Outcome);
    }

    private static void DeliverOnShutdown(IBackgroundTaskReportSink sink, BackgroundTaskOutcome outcome)
    {
        try
        {
            sink.DeliverOnShutdown(outcome);
        }
        catch (Exception e)
        {
            //一个收不了不能拖累其余的
            Log.Warning($"Settle background task on shutdown failed: id={outcome.Task.Id}: {e.Message}");
        }
    }

    private static async Task WatchAsync(BackgroundTask task, IBackgroundTaskReportSink sink)
    {
        BackgroundTaskOutcome outcome = await task.Completion.ConfigureAwait(false);
        lock (_lock)
        {
            if (_shuttingDown) return; //退出路径已经同步落过了
            _running.TryRemove(task.Id, out _);
            _undelivered[task.Id] = new Undelivered(outcome, sink);
        }

        Changed?.Invoke(task.OwnerSessionId);
        try
        {
            await sink.DeliverAsync(outcome).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error($"Deliver background task failed: id={task.Id} session={task.OwnerSessionId}: {e}");
        }
        finally
        {
            _undelivered.TryRemove(task.Id, out _);
        }
    }

    /// <summary>测试用：撤销退出标记（注册表是进程级静态的）</summary>
    internal static void ResetShutdownForTests()
    {
        lock (_lock) _shuttingDown = false;
    }

    private sealed record Entry(BackgroundTask Task, IBackgroundTaskReportSink Sink);

    private sealed record Undelivered(BackgroundTaskOutcome Outcome, IBackgroundTaskReportSink Sink);
}
