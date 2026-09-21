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
/// 一次服务调用结束了：它的请求与响应刚落进历史，此后流出来的内容属于<b>下一条</b>助手消息。
///
/// 存在的理由：一轮之内的多次服务调用在内容流里是<b>连着的</b>，没有任何天然分隔。
/// 界面从前靠两条启发式猜边界（遇到工具调用、think/text 切换），而"文本接文本"
/// ——正是插话之后那次调用的形状——两条都不命中，第二条回复就续进了上一条气泡里。
/// 由落盘这一刻发出边界，实时装配出来的形状与历史回放<b>按构造相等</b>。
///
/// 不进历史、不回喂模型。
/// </summary>
public sealed class MessageBoundaryContent : AIContent
{
    /// <summary>无状态，共用一个实例即可</summary>
    public static readonly MessageBoundaryContent Instance = new();

    private MessageBoundaryContent()
    {
    }
}
