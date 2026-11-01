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
/// 这一发确实在加密思考（流里带回了密文），现在把服务端报的思考 token 补到那张卡上。
///
/// 它<b>不</b>负责判定「是不是加密思考」——那个问题只有一个证据：密文本身
/// （chat 线的 <c>reasoning_details</c> 加密条目、responses 线的 <c>ProtectedData</c>）。
/// 「正文 0 字 + usage 报了思考 token」不是证据：明文思考的模型在工具调用发上就是
/// 不产生正文、usage 却仍报计数（实测 deepseek 报 1），按计数判会让界面凭空冒出一张
/// 「加密思考」卡（ADR 0100 修订）。
///
/// 所以它由 <see cref="LazyChatClient"/> 在流尾发出，只在<b>已看见密文</b>时才发；
/// 界面收到的是一张已经开着的卡（<see cref="EncryptedReasoningContent"/> 开的），这里只补数。
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
