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
/// 一次工具调用<b>内部</b>产生的活动内容,附带发起它的那次调用的标识,
/// 供界面把过程归到对应的工具卡片名下(点开卡片上的按钮在只读窗口里看)。
///
/// 为什么要单开一个内容类型而不直接把过程拼进工具返回值:子代理存在的理由就是让过程
/// <b>不进</b>主 agent 的上下文——工具返回值是模型能看见的东西,过程一旦写进去,委派就白做了。
/// 因此过程只走执行者的输出流(纯输出,历史落盘由 <c>SessionChatHistoryProvider</c> 另行负责),
/// 只被渲染,永不回喂模型。
///
/// 只有多轮的工具才值得走这条通道。<c>ViewImage</c> 刻意不接:它是单轮的,
/// 推出来的增量与返回值是同一份文本,接上只是把同一段话显示两遍。
/// </summary>
public sealed class ToolActivityContent : AIContent
{
    /// <summary>发起本次活动的工具调用标识(与 <see cref="FunctionCallContent.CallId"/> 同一值)</summary>
    public string CallId { get; }

    /// <summary>被包裹的实际内容(子代理的思考/正文/工具调用等)</summary>
    public AIContent Inner { get; }

    /// <param name="callId">发起本次活动的工具调用标识</param>
    /// <param name="inner">被包裹的实际内容</param>
    public ToolActivityContent(string callId, AIContent inner)
    {
        CallId = callId;
        Inner = inner;
    }
}
