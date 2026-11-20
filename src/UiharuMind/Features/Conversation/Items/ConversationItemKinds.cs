/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 条目类型 → 身份里的种类。只在「有来源但还没定身份」时兜底用：
/// 正常路径（转录器、回放）都会显式给出种类与序号，因为一条消息可以拆出多张同类卡。
/// </summary>
public static class ConversationItemKinds
{
    /// <summary>
    /// 按条目类型推断种类
    /// </summary>
    /// <param name="item">条目</param>
    /// <returns>种类</returns>
    public static EConversationItemKind Of(ConversationItemBase item) => item switch
    {
        ThinkingItem => EConversationItemKind.Thinking,
        ToolCallItem => EConversationItemKind.ToolCall,
        HandoffItem => EConversationItemKind.Handoff,
        ApprovalRequestItem => EConversationItemKind.Approval,
        ErrorItem => EConversationItemKind.Error,
        TextConversationItem { IsNarration: true } => EConversationItemKind.Narration,
        TextConversationItem => EConversationItemKind.Text,
        _ => EConversationItemKind.Other,
    };
}
