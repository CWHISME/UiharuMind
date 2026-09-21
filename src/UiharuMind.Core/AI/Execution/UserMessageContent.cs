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
/// 模型<b>此刻</b>消费了一条用户消息：本轮开头的那条输入，或运行中插的那一句
/// （它被注入队列在下一次服务调用开头取走）。
///
/// 存在的理由：内容流原本只有模型的产出，用户消息只能从历史读——而历史按服务调用粒度
/// 落盘，插话要等消费它的那次调用<b>结束</b>才落盘，那时回复早已流出来了。界面于是只能
/// 先乐观显示、后按正文认领，两条推演线各错过一次（"回答排在提问前面""同一句话两条"）。
/// 让消费方在消费的那一刻把它发进流里，用户消息与回复就在<b>同一条</b>有序流上，
/// 界面不再推演。
///
/// 界面按 <see cref="Message"/> 的<b>引用</b>去重：发送方自己那一格早就画了这条气泡
/// （发送时乐观显示），观察别人那一轮的窗口才需要据此画出来。
///
/// 不进历史、不回喂模型——与 <c>SubSessionStartedContent</c> 同一性质。
/// </summary>
public sealed class UserMessageContent : AIContent
{
    /// <summary>被消费的那条用户消息（与发送方持有的是同一个实例）</summary>
    public ChatMessage Message { get; }

    /// <summary>是运行中插的话（而不是本轮开头的输入）。子代理的报告要交代这一点</summary>
    public bool IsInterjection { get; }

    /// <param name="message">被消费的用户消息</param>
    /// <param name="isInterjection">是否为运行中插话</param>
    public UserMessageContent(ChatMessage message, bool isInterjection = false)
    {
        Message = message;
        IsInterjection = isInterjection;
    }
}
