/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 轮次之外往会话历史里写一条的那几处（子代理报告、后台任务结果）共用的写锁。
/// 历史是 <c>List&lt;ChatMessage&gt;</c>，各来源各持一把锁的话并发 Add/SaveAppended 照样交错
/// </summary>
internal static class SessionHistoryLocks
{
    private static readonly ConcurrentDictionary<string, object> _locks = new();

    /// <summary>
    /// 取某个会话的历史写锁
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>锁对象</returns>
    public static object For(string sessionId) => _locks.GetOrAdd(sessionId, _ => new object());
}
