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
/// SendMessage 起的等待任务共用的口径（单聊与发群两处都用这一份，免得各写一个数）
/// </summary>
internal static class WaitTaskLimits
{
    /// <summary>
    /// 等待的时间上限。单聊一轮正常几分钟，群一波实测十几分钟（ADR 0089），但长评审一波实测 73 分钟
    /// （2026-11-14 实机：一小时上限在群还在跑的时候就把等待掐了），四小时留三倍余量。
    /// 到点按超时收口，仍会送一封「没等到」的回信，不算断头
    /// </summary>
    public static readonly TimeSpan MaxRuntime = TimeSpan.FromHours(4);
}
