/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 消息「在界面上长什么样」的口径：哪些不画、用户气泡显示哪段话。
/// 会话流渲染与会话内搜索共用这一处——两边各写一份的时候已经分叉过一次（审批回应搜得到、看不见）
/// </summary>
public static class ChatMessageDisplay
{
    /// <summary>
    /// 识别非真实用户输入的 user 角色消息：框架上下文提供器注入的消息
    /// （todo 快照、模式切换通知等）带 <see cref="ChatMessageAnnotations.Attribution"/> 溯源标记；审批回应为控制消息。
    /// 它们是模型上下文的一部分（持久化属正常），但不应渲染为用户气泡。
    /// 点名调用（/技能名）是例外：它明确定义为要落盘 + 渲染成折叠气泡，即便历史副本
    /// 被框架回灌时盖上了溯源标记，也不该被当成框架注入滤掉。
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>是否为框架注入</returns>
    public static bool IsFrameworkInjected(ChatMessage message)
    {
        if (ChatMessageAnnotations.NamedSkillInputOf(message) != null) return false;
        // 带自家标记的消息(群投递、私聊…)会被框架回灌历史时就地盖上「来源 = 历史」的溯源标记,
        // 它们本来就是我们的历史;按键在不在判的话,下一轮一跑、对账一重放就被当成注入消息藏掉
        if (message.AdditionalProperties?.ContainsKey(ChatMessageAnnotations.Attribution) == true)
            return !ChatMessageAnnotations.IsHistoryEcho(message);
        return message.Contents.Any(x => x is ToolApprovalResponseContent);
    }

    /// <summary>条目与标题用的显示文本：点名调用取用户敲的那一行，其余取消息正文</summary>
    /// <param name="message">消息</param>
    /// <returns>显示文本</returns>
    public static string TextOf(ChatMessage message) =>
        ChatMessageAnnotations.NamedSkillInputOf(message) ?? PlainTextOf(message);

    /// <summary>不看点名调用的正文：群成员私聊发给模型时前面带一句私聊说明，显示的是用户打的原话</summary>
    /// <param name="message">消息</param>
    /// <returns>显示文本</returns>
    public static string PlainTextOf(ChatMessage message) =>
        ChatMessageAnnotations.IsGroupPrivate(message) ? GroupTranscript.StripPrivateNote(message.Text) : message.Text;
}
