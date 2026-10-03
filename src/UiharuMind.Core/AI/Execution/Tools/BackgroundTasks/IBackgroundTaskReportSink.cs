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
/// 后台任务结束后，结果送到哪儿、要不要叫醒谁。按会话形态由装配处选定（单聊与群成员的叫醒方式不同），
/// 注册表只管进程，不认识会话形态
/// </summary>
public interface IBackgroundTaskReportSink
{
    /// <summary>
    /// 任务结束：把结果落进会话并按需起一轮唤醒
    /// </summary>
    /// <param name="outcome">结局</param>
    Task DeliverAsync(BackgroundTaskOutcome outcome);

    /// <summary>
    /// 应用退出时同步落一条「被中止」，不唤醒（进程马上就没了）
    /// </summary>
    /// <param name="outcome">结局</param>
    void DeliverOnShutdown(BackgroundTaskOutcome outcome);
}
