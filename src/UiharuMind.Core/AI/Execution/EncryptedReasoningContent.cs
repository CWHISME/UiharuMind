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
/// 模型正在思考，但<b>一个字都没有下发</b>（加密思考，如部分闭源模型）。
///
/// 存在的理由：思考卡按内容增量创建，没有正文就一张都不会出现，整段思考期界面上空着，
/// 读起来像模型没在干活。而"想过没有"的可靠证据在流里就有——加密条目随流到达，
/// 只是它在适配层就被丢了（MEAI 只认 <c>reasoning_content</c>），到不了这里。
/// 所以由 HTTP 层观测、<see cref="LazyChatClient"/> 转成这条内容，让界面<b>立刻</b>开一张没有正文的思考卡。
///
/// 与 <see cref="MessageBoundaryContent"/> 同一条路：随流走、由转录器认领。
///
/// <b>但它现在还带着票据</b>（ADR 0100）：落盘前不再摘掉，而是转成一条带
/// <see cref="TextReasoningContent.ProtectedData"/> 的思考内容，随消息存进历史。
/// 这样票据<b>长在消息上</b>，重启后读历史即得，不需要重建任何映射——
/// 序号映射那套（0100 的 <c>TicketStore</c>）在历史一裁剪、一替换时就会错位。
/// </summary>
public sealed class EncryptedReasoningContent : AIContent
{
    /// <summary>
    /// 票据原文（整个 <c>reasoning_details</c> 数组）。为 null 时是纯信号内容，不带票据。
    /// </summary>
    public string? TicketJson { get; init; }

    /// <summary>产出这张票的供应商</summary>
    public string? ProducerProvider { get; init; }

    /// <summary>产出这张票的模型</summary>
    public string? ProducerModel { get; init; }

    /// <summary>无状态，共用一个实例即可</summary>
    public static readonly EncryptedReasoningContent Instance = new();

    private EncryptedReasoningContent()
    {
    }

    /// <summary>
    /// 带票据的内容。票据密文不能解析后再编码，所以这里存原文。
    /// </summary>
    /// <param name="ticketJson">票据原文</param>
    /// <param name="producerProvider">产出它的供应商</param>
    /// <param name="producerModel">产出它的模型</param>
    public static EncryptedReasoningContent WithTicket(string ticketJson, string producerProvider, string producerModel) =>
        new() { TicketJson = ticketJson, ProducerProvider = producerProvider, ProducerModel = producerModel };
}
