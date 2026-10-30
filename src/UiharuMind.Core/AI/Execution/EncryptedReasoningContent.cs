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
/// 模型正在思考，但<b>一个字都没有下发</b>（加密思考，如 <c>meta/muse-spark</c>）。
///
/// 存在的理由：思考卡按内容增量创建，没有正文就一张都不会出现，整段思考期界面上空着，
/// 读起来像模型没在干活。而"想过没有"的可靠证据在流里就有——加密条目随流到达，
/// 只是它在适配层就被丢了（MEAI 只认 <c>reasoning_content</c>），到不了这里。
/// 所以由 HTTP 层观测、<c>LazyChatClient</c> 转成这条内容，让界面<b>立刻</b>开一张没有正文的思考卡。
///
/// 与 <see cref="MessageBoundaryContent"/> 同一条路：随流走、由转录器认领，
/// <b>不进历史、不回喂模型</b>（落库前由 <c>SessionChatHistoryProvider</c> 摘掉）。
/// </summary>
public sealed class EncryptedReasoningContent : AIContent
{
    /// <summary>无状态，共用一个实例即可</summary>
    public static readonly EncryptedReasoningContent Instance = new();

    private EncryptedReasoningContent()
    {
    }
}
