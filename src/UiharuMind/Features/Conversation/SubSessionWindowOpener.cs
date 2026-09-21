/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.QuickChat;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 打开一个子会话。<b>全仓只有这一个入口</b>——工具卡片上的「查看过程」与右栏
/// 「子代理」面板点进去是同一个动作。
///
/// 刻意不为子会话另造窗口：它就是一个会话，<see cref="QuickChatViewWindow"/> 已经是
/// 「给一个会话，还你一个能看能聊的浮窗」。跑着还是跑完了由
/// <c>ConversationViewModel</c> 自己判（外驱模式），窗口这一层不需要知道。
/// </summary>
public static class SubSessionWindowOpener
{
    /// <summary>
    /// 打开子会话窗口
    /// </summary>
    /// <param name="subSessionId">子会话标识</param>
    public static void Open(string? subSessionId)
    {
        if (string.IsNullOrEmpty(subSessionId)) return;

        ChatSession? session = SessionManager.Instance.Load(subSessionId);
        if (session == null)
        {
            // 派活者被删时子会话随之级联删除，所以这里多半意味着历史里那条记录比磁盘旧
            Log.Warning($"Sub-session '{subSessionId}' no longer exists.");
            return;
        }

        QuickChatViewWindow.Show(session);
    }
}
