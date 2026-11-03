/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 会话流的历史来源：执行者优先，给不出就回落会话本体。
///
/// 两处都可能给空：执行者还没挂接、挂接与绑定之间的间隙、装配迁移失败把绑定清掉，
/// 以及会话已被删除。这些都不是「没有历史」，而窗口拿空表当「已到开头」就会卡死
/// （见 <see cref="ConversationHistoryPager"/> 的 TakeHistory）。
///
/// 规则单独放一处是为了可测：它错一次的表现是窗口与内容错位，那种状态只能靠截图发现
/// </summary>
internal static class ConversationWindowHistory
{
    /// <summary>
    /// 选这一眼要用的历史
    /// </summary>
    /// <param name="runnerHistory">执行者这一眼给出的历史；没挂接时为 null</param>
    /// <param name="session">当前会话本体；已删除时为 null</param>
    /// <returns>窗口用的历史；两处都拿不到时为空表（由开窗那边守住不动窗口）</returns>
    public static IReadOnlyList<ChatMessage> Choose(IReadOnlyList<ChatMessage>? runnerHistory, ChatSession? session) =>
        runnerHistory is { Count: > 0 } ? runnerHistory : session?.History.Snapshot() ?? [];
}
