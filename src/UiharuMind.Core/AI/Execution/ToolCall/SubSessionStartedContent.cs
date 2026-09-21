/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.ToolCall;

/// <summary>
/// 一次委派开始了：把新建子会话的标识交给界面，好让那张工具卡片<b>此刻</b>就能点开它。
///
/// <b>它是一次性的标识交接，不是内容流。</b> 子代理的过程不经这里——界面看子会话读的是
/// 子会话自己的历史（<c>ChatSession.HistoryAppended</c>），全仓只有那一份真相。
/// 前身是 <c>ToolActivityContent</c>，那个把整条过程流转发进父会话的界面缓冲里，
/// 于是同一批内容有了两个住处、且重启即失（见 ADR 0021）。
///
/// 之所以仍然要这一条：子会话标识虽然也缀在工具结果里（<c>[sub-session: …]</c>），
/// 但结果要等子代理跑完才有，而"跑着的时候点开看看"正是这件事的重点。
///
/// 不进历史、不回喂模型——与其它经执行者输出流的界面通知同一性质。
/// </summary>
public sealed class SubSessionStartedContent : AIContent
{
    /// <summary>派出它的那次工具调用标识（界面据此找到对应卡片）</summary>
    public string CallId { get; }

    /// <summary>新建的子会话标识</summary>
    public string SubSessionId { get; }

    /// <param name="callId">工具调用标识</param>
    /// <param name="subSessionId">子会话标识</param>
    public SubSessionStartedContent(string callId, string subSessionId)
    {
        CallId = callId;
        SubSessionId = subSessionId;
    }
}
