/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.Scheduler;

/// <summary>
/// 定时任务状态
/// </summary>
public enum EScheduledTaskStatus
{
    /// <summary>等待触发</summary>
    Pending,

    /// <summary>执行中</summary>
    Running,

    /// <summary>执行完成</summary>
    Completed,

    /// <summary>执行失败</summary>
    Failed,

    /// <summary>已取消</summary>
    Cancelled,

    /// <summary>错过触发时间,等待用户决定补跑或跳过</summary>
    Missed,
}

/// <summary>
/// 错过触发时间的处理策略(第一版仅实现 PromptUser)
/// </summary>
public enum EMissedFirePolicy
{
    /// <summary>提示用户决定</summary>
    PromptUser,

    /// <summary>直接跳过</summary>
    Skip,

    /// <summary>立即补跑</summary>
    RunImmediately,
}

/// <summary>
/// 定时任务:本质是"延迟启动的 agent 会话"。
/// 创建时固化权限档与预授权 shell 命令模式,无人值守执行时其余危险操作一律自动拒绝。
/// </summary>
public class ScheduledAgentTask
{
    /// <summary>任务唯一标识</summary>
    public string TaskId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>显示名</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>触发时间</summary>
    public DateTimeOffset FireAt { get; set; }

    /// <summary>执行提示词(交给 agent 的任务描述)</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>绑定的工作目录</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>创建时固化的预授权 shell 命令 glob 模式</summary>
    public List<string> PreAuthorizedCommands { get; init; } = new();

    /// <summary>
    /// 无人值守执行时用的权限档(语义见 <see cref="ApprovalModeMapper"/>)。
    ///
    /// 初始值 <b>必须</b>是 <see cref="EAgentPermissionMode.AutoEdit"/> 而不是枚举的 default
    /// (那是 ReadOnly):这个字段是后加的,旧存档里没有它,反序列化时 System.Text.Json 不会碰
    /// 缺失的属性,于是初始值就是旧任务的档位——这一条把"加字段"变成了行为零变化。
    ///
    /// 只有用户能改它。定时任务没人盯着,让模型自己在创建时把档位提到"完全自动"
    /// 等于绕开了 ADR 0010 说的那条真正边界("用户点了那一下")。
    /// </summary>
    public EAgentPermissionMode PermissionMode { get; set; } = EAgentPermissionMode.AutoEdit;

    /// <summary>错过触发时间的策略</summary>
    public EMissedFirePolicy MissedFirePolicy { get; set; } = EMissedFirePolicy.PromptUser;

    /// <summary>当前状态</summary>
    public EScheduledTaskStatus Status { get; set; } = EScheduledTaskStatus.Pending;

    /// <summary>执行产生的会话标识,可回看执行过程</summary>
    public string? ResultSessionId { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// 调度后端抽象:第一版为进程内实现,系统级注册(launchd/计划任务)留作后续后端
/// </summary>
public interface ISchedulerBackend
{
    /// <summary>后端标识</summary>
    string BackendId { get; }

    /// <summary>任务状态变化通知</summary>
    event Action<ScheduledAgentTask>? OnTaskUpdated;

    /// <summary>
    /// 登记一个任务
    /// </summary>
    /// <param name="task">任务</param>
    Task ScheduleAsync(ScheduledAgentTask task);

    /// <summary>
    /// 取消一个任务
    /// </summary>
    /// <param name="taskId">任务标识</param>
    Task CancelAsync(string taskId);

    /// <summary>
    /// 立即执行一个任务(补跑等场景)
    /// </summary>
    /// <param name="taskId">任务标识</param>
    Task RunNowAsync(string taskId);

    /// <summary>
    /// 改一个还没跑的任务的权限档。已经跑过的任务改了没有意义,实现应当忽略。
    /// </summary>
    /// <param name="taskId">任务标识</param>
    /// <param name="mode">权限档</param>
    Task SetPermissionModeAsync(string taskId, EAgentPermissionMode mode);

    /// <summary>
    /// 获取全部任务(含历史)
    /// </summary>
    /// <returns>任务列表</returns>
    IReadOnlyList<ScheduledAgentTask> GetTasks();
}
