/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

/// <summary>后台任务是怎么结束的</summary>
public enum EBackgroundTaskEnd
{
    /// <summary>进程自己退出</summary>
    Exited,

    /// <summary>到了时间上限，连同进程树被结束</summary>
    TimeLimit,

    /// <summary>用户在界面上叫停</summary>
    Stopped,

    /// <summary>应用退出时被中止</summary>
    AppExit,
}

/// <summary>
/// 一次后台任务的结局
/// </summary>
/// <param name="Task">任务</param>
/// <param name="End">结束方式</param>
/// <param name="ExitCode">退出码；被结束时可能取不到</param>
/// <param name="Duration">实际运行时长</param>
public sealed record BackgroundTaskOutcome(BackgroundTask Task, EBackgroundTaskEnd End, int? ExitCode, TimeSpan Duration);
