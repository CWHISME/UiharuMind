/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Execution.Delivery;

/// <summary>
/// 送信要碰的外界：取会话、问忙闲、拿执行者、起唤醒轮。抽出来是为了测得到「插进一轮、没消费就回退」那一段
/// </summary>
internal interface ISessionDeliveryHost
{
    /// <summary>会话正忙时隔多久再看一次</summary>
    TimeSpan BusyRetryInterval { get; }

    /// <summary>按标识取会话，没有为 null</summary>
    ChatSession? Load(string sessionId);

    /// <summary>会话有没有轮次在跑</summary>
    bool IsBusy(string sessionId);

    /// <summary>会话的执行者</summary>
    ICharacterRunner RunnerOf(ChatSession session);

    /// <summary>起一轮唤醒轮</summary>
    Task WakeAsync(string sessionId, string cause);
}
