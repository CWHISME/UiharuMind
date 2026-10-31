/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 这一发报了思考 token，却<b>一个字正文都没回来</b>（闭源模型只回密文、或只报计数）。
///
/// 判据只有两样，而且都在同一发流里：思考正文字数与用量里的 <c>reasoning_tokens</c>。
/// 所以它由 <see cref="LazyChatClient"/> 在流尾发出，<b>不</b>由上层拿历史去猜——那一套要靠
/// 「边界、落盘、用量」三者的先后，实测排不齐（响应消息偶尔晚一次落盘、用量被挪到流尾），
/// 猜错的症状是把上一发的 token 盖到下一发的消息上，界面凭空冒出一张「加密思考」卡。
///
/// 与 <see cref="EncryptedReasoningContent"/> 同一条路：随流走、由转录器认领；
/// 落盘时转成消息上的 <c>_thinkingUnavailableTokens</c> 标记并摘掉自己，
/// 于是重开会话时卡还能按标记补回来——标记长在消息上，不需要任何映射。
/// </summary>
public sealed class ThinkingNotReturnedContent : AIContent
{
    /// <summary>服务端为这一发报的思考 token</summary>
    public long ReasoningTokens { get; }

    /// <summary>
    /// 造一条信号
    /// </summary>
    /// <param name="reasoningTokens">服务端报的思考 token 数</param>
    public ThinkingNotReturnedContent(long reasoningTokens)
    {
        ReasoningTokens = reasoningTokens;
    }
}
