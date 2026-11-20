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
/// 条目在「一条消息拆出的若干条目」里的种类。身份的第二段：同一条消息会拆出思考卡、正文气泡、
/// 若干工具卡，只有种类 + 序号才能把它们区分开。
/// </summary>
public enum EConversationItemKind
{
    /// <summary>正文气泡（助手或用户）</summary>
    Text,

    /// <summary>思考卡</summary>
    Thinking,

    /// <summary>工具调用卡</summary>
    ToolCall,

    /// <summary>知识库检索卡</summary>
    Knowledge,

    /// <summary>交接文档卡</summary>
    Handoff,

    /// <summary>旁白（开场白、子代理来信、后台任务结果）</summary>
    Narration,

    /// <summary>审批卡</summary>
    Approval,

    /// <summary>错误卡</summary>
    Error,

    /// <summary>不属于任何历史消息的界面条目（占位、提示）</summary>
    Other,
}
