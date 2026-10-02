using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Utils;

/// <summary>不等的后台 Task：不等它，但它出错要留日志，不能被静默吞掉</summary>
public static class TaskFaults
{
    /// <summary>
    /// 放手让它跑，出错时记一条日志
    /// </summary>
    /// <param name="task">后台 Task</param>
    /// <param name="what">它在做什么（日志里说「failed to {what}」）</param>
    public static void LogOnFault(this Task task, string what) =>
        task.ContinueWith(x => Log.Error($"Failed to {what}: {x.Exception}"), CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
}
