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
    /// <param name="hit">命中</param>
    public ConversationSearchHitRow(SessionSearchHit hit)
    {
        Hit = hit;
        Label = LabelOf(hit);
    }

    /// <summary>命中</summary>
    public SessionSearchHit Hit { get; }

    /// <summary>来源标签：我 / 回复 / 思考 / 工具</summary>
    public string Label { get; }

    /// <summary>命中前后的一小段</summary>
    public string Snippet => Hit.Snippet;

    private static string LabelOf(SessionSearchHit hit) => hit.Kind switch
    {
        ESearchHitKind.Thinking => Loc.Text(LangKey.ConversationSearchThinking),
        ESearchHitKind.Tool => Loc.Text(LangKey.ConversationSearchTools),
        _ => Loc.Text(hit.Message.Role == ChatRole.User
            ? LangKey.ConversationSearchKindUser
            : LangKey.ConversationSearchKindReply),
    };
}
