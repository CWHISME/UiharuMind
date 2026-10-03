/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

/// <summary>
/// 把一条命令放到后台跑，跑完把结果送回并叫醒启动者。
///
/// 只有一把：看进度用 Read 读日志；中途叫停交给用户（历史里模型主动叫停自己后台任务的只有一次），
/// 不会自己退出的程序靠 <c>maxSeconds</c> 收掉
/// </summary>
public static class BackgroundTaskTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量</summary>
    public const string ToolName = "StartBackgroundTask";

    /// <summary>不传时的时间上限。历史里最长的后台任务约 30 分钟，一小时盖得住，又不至于让忘掉的进程挂一夜</summary>
    internal static readonly TimeSpan DefaultMaxRuntime = TimeSpan.FromHours(1);

    private static readonly TimeSpan MaxAllowedRuntime = TimeSpan.FromHours(24);

    /// <summary>
    /// 创建后台任务工具，审批与 Shell 同一套
    /// </summary>
    /// <param name="ownerSessionId">启动者的会话标识，结果送回这里</param>
    /// <param name="launch">起跑环境</param>
    /// <param name="sink">结果送到哪儿</param>
    /// <returns>需审批的工具</returns>
    public static AIFunction Create(string ownerSessionId, BackgroundTaskLaunch launch, IBackgroundTaskReportSink sink)
    {
        AIFunction function = AIFunctionFactory.Create(
            ([Description("The shell command to run, same syntax as the Shell tool.")]
                string command,
                [Description("One short line saying what this task is; shown to the user.")]
                string description,
                [Description("Optional time limit in seconds; the whole process tree is stopped when it is reached.")]
                int? maxSeconds = null) => Start(ownerSessionId, launch, sink, command, description, maxSeconds),
            ToolName,
            "Run a shell command in the background and return right away. " +
            "Use it for commands that may take longer than a few minutes (builds, full test or regression runs) " +
            "and for programs that never exit on their own (servers, GUI apps; give them maxSeconds). " +
            "When the task ends, its exit code and the tail of its output are sent back to you as a message " +
            "and you are woken up, so do not poll or sleep waiting for it. " +
            "To check progress before that, read the log file it returns. " +
            $"Without maxSeconds it is stopped after {DefaultMaxRuntime.TotalSeconds:0} seconds. " +
            "This tool already runs in the background: do not add `&`, `nohup` or `open` to the command, " +
            "or whatever they detach can no longer be stopped or time-limited.");
        return new ApprovalRequiredAIFunction(function);
    }

    private static string Start(string ownerSessionId, BackgroundTaskLaunch launch, IBackgroundTaskReportSink sink,
        string command, string description, int? maxSeconds)
    {
        if (string.IsNullOrWhiteSpace(command)) return "Error: command must not be empty.";

        TimeSpan maxRuntime = maxSeconds is > 0
            ? TimeSpan.FromSeconds(Math.Min(maxSeconds.Value, MaxAllowedRuntime.TotalSeconds))
            : DefaultMaxRuntime;
        string shown = string.IsNullOrWhiteSpace(description) ? command : description.Trim();

        try
        {
            BackgroundTask task = BackgroundTaskRegistry.Start(ownerSessionId, command, shown, maxRuntime, launch, sink);
            return $"Started background task {task.Id}. NO RESULT YET. " +
                   $"Its result arrives on its own when it ends (time limit {maxRuntime.TotalSeconds:0}s); do not poll for it.\n" +
                   $"Log: {task.LogPath}";
        }
        catch (Exception e)
        {
            Log.Warning($"Start background task failed: session={ownerSessionId}: {e.Message}");
            return $"Error: could not start the task: {e.Message}";
        }
    }
}
