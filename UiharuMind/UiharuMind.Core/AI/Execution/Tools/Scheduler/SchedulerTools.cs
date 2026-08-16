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

namespace UiharuMind.Core.AI.Execution.Tools.Scheduler;

/// <summary>
/// 定时任务相关工具。create_scheduled_task 属危险操作,包装为需审批工具:
/// 用户批准创建 = 授予任务附带的预授权命令,到点无人值守执行。
///
/// <b>刻意不提供权限档参数</b>:新任务一律落在 <see cref="ScheduledAgentTask.PermissionMode"/>
/// 的初始档(AutoEdit),要提到"完全自动"只能由用户在任务列表里改。定时任务到点没人盯着,
/// 让模型自己给自己升档就绕开了 ADR 0010 说的那条真正边界——"用户点了那一下"。
/// </summary>
public static class SchedulerTools
{
    /// <summary>工具名。提示词与卡片图标等处提到本工具时一律引用这个常量</summary>
    public const string ToolName = "create_scheduled_task";

    /// <summary>
    /// 创建"登记定时任务"工具
    /// </summary>
    /// <param name="workspacePath">当前会话绑定的工作目录(任务继承)</param>
    /// <returns>需审批的工具实例</returns>
    public static AITool CreateScheduledTaskTool(string? workspacePath)
    {
        AIFunction function = AIFunctionFactory.Create(
            (
                [Description("Short human readable task name.")]
                string displayName,
                [Description("Task instruction for the agent to execute when fired.")]
                string prompt,
                [Description("Fire after N minutes from now. Ignored when fireAtIso is provided.")]
                double? delayMinutes,
                [Description("Absolute fire time (ISO 8601). Overrides delayMinutes.")]
                string? fireAtIso,
                [Description("Shell command glob patterns the task is pre-authorized to run unattended, " +
                             "e.g. [\"git add*\", \"git commit*\"]. Anything else will be denied at run time.")]
                string[]? preAuthorizedCommands
            ) => CreateTaskAsync(workspacePath, displayName, prompt, delayMinutes, fireAtIso, preAuthorizedCommands),
            ToolName,
            "Schedule an agent task to run automatically at a future time " +
            "(e.g. 'commit the repo in 30 minutes'). List every shell command pattern the task will need " +
            "in preAuthorizedCommands - unattended execution denies everything else.");

        return new ApprovalRequiredAIFunction(function);
    }

    private static async Task<string> CreateTaskAsync(string? workspacePath, string displayName, string prompt,
        double? delayMinutes, string? fireAtIso, string[]? preAuthorizedCommands)
    {
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(prompt))
            return "Error: displayName and prompt are required.";

        DateTimeOffset fireAt;
        if (!string.IsNullOrEmpty(fireAtIso))
        {
            if (!DateTimeOffset.TryParse(fireAtIso, out fireAt)) return $"Error: invalid fireAtIso '{fireAtIso}'.";
        }
        else if (delayMinutes is > 0)
        {
            fireAt = DateTimeOffset.Now.AddMinutes(delayMinutes.Value);
        }
        else
        {
            return "Error: provide either delayMinutes (> 0) or fireAtIso.";
        }

        ScheduledAgentTask task = new()
        {
            DisplayName = displayName,
            FireAt = fireAt,
            Prompt = prompt,
            WorkspacePath = workspacePath,
            PreAuthorizedCommands = preAuthorizedCommands?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new(),
        };
        await CharacterRunnerFactory.Instance.Scheduler.ScheduleAsync(task).ConfigureAwait(false);

        return $"Scheduled task '{displayName}' (id {task.TaskId}) to fire at {fireAt:yyyy-MM-dd HH:mm:ss zzz}. " +
               $"Permission mode: {task.PermissionMode} (the user can raise it to FullAuto in the task list). " +
               $"Pre-authorized shell patterns: [{string.Join(", ", task.PreAuthorizedCommands)}]";
    }
}
