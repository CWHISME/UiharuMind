/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

/// <summary>
/// 后台任务的最小面：注册表不认识任务的实现——shell 进程（<see cref="BackgroundTask"/>）
/// 与进程内委托（<see cref="InlineBackgroundTask"/>）都长这样。
///
/// 报告与送达只读 <see cref="Id"/> / <see cref="OwnerSessionId"/> / <see cref="Description"/> /
/// <see cref="LogPath"/> / <see cref="ResultText"/>，结局由 <see cref="Completion"/> 交底。
/// </summary>
public interface IBackgroundTask
{
    /// <summary>任务编号</summary>
    string Id { get; }

    /// <summary>启动它的会话标识，结果送回这里</summary>
    string OwnerSessionId { get; }

    /// <summary>一句话说明，显示给用户</summary>
    string Description { get; }

    /// <summary>日志文件的绝对路径；没有日志文件（inline）为空串</summary>
    string LogPath { get; }

    /// <summary>时间上限</summary>
    TimeSpan MaxRuntime { get; }

    /// <summary>启动时刻</summary>
    DateTimeOffset StartedAt { get; }

    /// <summary>结局（结束时完成；只完成一次）</summary>
    Task<BackgroundTaskOutcome> Completion { get; }

    /// <summary>
    /// 结果正文。shell 任务为 null（报告读日志尾巴）；inline 任务非 null——
    /// 等待器攒好的结论就是这个任务的全部结果
    /// </summary>
    string? ResultText { get; }

    /// <summary>
    /// 结束这个任务。shell 任务是杀进程树；inline 任务是取消它的执行体并记下原因。
    /// 已经结束的不受影响，先到的原因为准
    /// </summary>
    /// <param name="reason">结束原因</param>
    void Kill(EBackgroundTaskEnd reason);
}