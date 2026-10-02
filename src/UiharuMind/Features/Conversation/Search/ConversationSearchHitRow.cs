/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Search;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Search;

/// <summary>结果列表里的一行：谁说的 / 哪一部分，加命中前后那一小段</summary>
public sealed class ConversationSearchHitRow
{
    /// <summary>构造</summary>
    /// <param name="hit">命中</param>
    public ConversationSearchHitRow(SessionSearchHit hit)
    {
        Hit = hit;
        Label = LabelOf(hit.Kind, hit.Message.Role == ChatRole.User);
    }

    /// <summary>命中</summary>
    public SessionSearchHit Hit { get; }

    /// <summary>来源标签：我 / 回复 / 思考 / 工具</summary>
    public string Label { get; }

    /// <summary>命中前后的一小段</summary>
    public string Snippet => Hit.Snippet;

    /// <summary>来源标签（跨会话搜索的摘要行同一口径）</summary>
    /// <param name="kind">落在哪一部分</param>
    /// <param name="isUser">是不是用户说的</param>
    /// <returns>我 / 回复 / 思考 / 工具</returns>
    internal static string LabelOf(ESearchHitKind kind, bool isUser) => kind switch
    {
        ESearchHitKind.Thinking => Loc.Text(LangKey.ConversationSearchThinking),
        ESearchHitKind.Tool => Loc.Text(LangKey.ConversationSearchTools),
        _ => Loc.Text(isUser ? LangKey.ConversationSearchKindUser : LangKey.ConversationSearchKindReply),
    };
}
