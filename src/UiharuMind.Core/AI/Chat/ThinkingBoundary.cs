/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 内容流里的思考边界：每一段内容对思考段而言是延续、终结还是无关。
/// 全仓唯一的来源——转录器（渲染）、计时器（落盘）与快捷窗口都走这里，
/// 两边对「什么算思考」永远一致；加新内容类型只改这一处。
/// parser 由调用方持有（流式扣留状态不能共享，各转各的）。
/// </summary>
public static class ThinkingBoundary
{
    /// <summary>
    /// 按思考边界分发一段内容：思考增量走 <paramref name="onThinking"/>，
    /// 正文原文走 <paramref name="onText"/>，终结思考段的走 <paramref name="onBreak"/>，
    /// 其余（用量/工具结果/子会话通知/错误）三方都不叫——与转录器原来的口径一致。
    /// </summary>
    /// <param name="content">内容流里的一段</param>
    /// <param name="parser">&lt;think&gt; 解析器（调用方持有）</param>
    /// <param name="onText">正文回调（转录器拿去上屏，计时器直接终结思考段）</param>
    /// <param name="onThinking">思考回调</param>
    /// <param name="onBreak">终结思考段（转录器是收段，计时器是收段计时）</param>
    public static void Dispatch(AIContent content, ThinkTagStreamParser parser,
        Action<string> onText, Action<string> onThinking, Action onBreak)
    {
        switch (content)
        {
            case TextReasoningContent { Text.Length: > 0 } reasoning:
                onThinking(reasoning.Text);
                break;
            case TextContent { Text.Length: > 0 } text:
                // 本地/部分远程模型把 <think> 混在正文流里，经解析器分离
                parser.Feed(text.Text, onText, onThinking);
                break;
            case FunctionCallContent:
            case MessageBoundaryContent:
            case ToolApprovalRequestContent:
            case UserMessageContent:
                onBreak();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// 收尾当前流段：先把解析器扣留的半截标签定夺（与转录器的 CloseSegment 对齐），再终结思考段。
    /// </summary>
    /// <param name="parser">&lt;think&gt; 解析器（调用方持有）</param>
    /// <param name="onText">正文回调</param>
    /// <param name="onThinking">思考回调</param>
    /// <param name="onBreak">终结思考段</param>
    public static void Complete(ThinkTagStreamParser parser,
        Action<string> onText, Action<string> onThinking, Action onBreak)
    {
        parser.Complete(onText, onThinking);
        onBreak();
    }
}
