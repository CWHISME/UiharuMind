/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Features.Conversation.Search;

/// <summary>跨会话搜索点进来要跳的那条</summary>
/// <param name="Query">关键词</param>
/// <param name="MessageIndex">命中在历史文件里的下标</param>
public sealed record ConversationSearchReveal(string Query, int MessageIndex);
