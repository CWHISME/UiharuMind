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
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

/// <summary>
/// 把一条命令放到后台跑，跑完把结果送回并叫醒启动者。
///
/// 只有一把：看进度用 Read 读日志；不会自己退出的程序靠 <c>maxSeconds</c> 收掉。
/// 没有给模型停止工具：它会 <c>ps | grep | kill</c> 自己停，杀掉根进程后照常收尾、通知照常送回
/// （要不要补一把 <c>TaskStop</c> 见 ADR 0065 待议）
/// </summary>
public static class BackgroundTaskTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量</summary>
    public const string ToolName = "RunBackgroundTask";

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
        AIFunction function = ToolJson.CreateFunction(
            ([Description("要运行的 shell 命令，语法与 Shell 工具相同。")]
                string command,
                [Description("一句话说明这个任务是什么；会显示给用户。")]
                string description,
                [Description("可选的时间上限（秒）；到达后整个进程树会被终止。")]
                int? maxSeconds = null) => Start(ownerSessionId, launch, sink, command, description, maxSeconds),
            ToolName,
            "在后台运行一条 shell 命令并立即返回。" +
            $"Shell 工具本身最多等待 {ShellExecutorFactory.CommandTimeout.TotalMinutes:0} 分钟并直接返回输出，" +
            "所以本工具只用于预期要跑更久、或不会退出的命令（服务器、GUI 程序；请设 maxSeconds）。" +
            "结束时退出码与输出末尾会以消息形式送回给你，所以不要轮询；" +
            "读返回的日志文件查进度即可。" +
            $"不传 maxSeconds 时会在 {DefaultMaxRuntime.TotalSeconds:0} 秒后停止。" +
            "不要加 `&`、`nohup` 或 `open`：凡是被它们脱离的东西都无法再被停止或限时。");
        return new ApprovalRequiredAIFunction(new ShellSelfKillGuardFunction(function));
    }

    private static string Start(string ownerSessionId, BackgroundTaskLaunch launch, IBackgroundTaskReportSink sink,
        string command, string description, int? maxSeconds)
    {
        if (string.IsNullOrWhiteSpace(command)) return "Error: 命令不能为空。";

        TimeSpan maxRuntime = maxSeconds is > 0
            ? TimeSpan.FromSeconds(Math.Min(maxSeconds.Value, MaxAllowedRuntime.TotalSeconds))
            : DefaultMaxRuntime;
        string shown = string.IsNullOrWhiteSpace(description) ? command : description.Trim();

        try
        {
            BackgroundTask task = BackgroundTaskRegistry.Start(ownerSessionId, command, shown, maxRuntime, launch, sink);
            return $"已启动后台任务 {task.Id}。尚无结果。" +
                   $"结束时会自动送回结果（时限 {maxRuntime.TotalSeconds:0} 秒）；不要轮询。\n" +
                   $"日志：{task.LogPath}";
        }
        catch (Exception e)
        {
            Log.Warning($"Start background task failed: session={ownerSessionId}: {e.Message}");
            return $"Error: 无法启动任务：{e.Message}";
        }
    }
}
