/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using System.Linq;
using UiharuMind.Core.AI.Chat.Search;
using UiharuMind.Features.Conversation.Search;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.SessionList;

/// <summary>「消息里提到的」那一段里的一个会话</summary>
public sealed class SessionContentResultRow
{
    /// <summary>构造</summary>
    /// <param name="match">这个会话的命中</param>
    /// <param name="session">会话条目（标题跟着改名走）</param>
    /// <param name="query">扫出这一行的关键词</param>
    public SessionContentResultRow(SessionContentMatch match, SessionListItem session, string query)
    {
        Session = session;
        CountText = Loc.Text(LangKey.ConversationSearchCount, match.HitCount);
        Hits = match.Latest.Select(x => new SessionContentHitRow(match.SessionId, x, query)).ToList();
    }

    /// <summary>会话条目</summary>
    public SessionListItem Session { get; }

    /// <summary>会话标识</summary>
    public string SessionId => Session.SessionId;

    /// <summary>「12 条」</summary>
    public string CountText { get; }

    /// <summary>最近几次提及，新到旧</summary>
    public IReadOnlyList<SessionContentHitRow> Hits { get; }
}

/// <summary>「消息里提到的」那一段里的一条摘要</summary>
public sealed class SessionContentHitRow
{
    /// <summary>构造</summary>
    /// <param name="sessionId">所在会话</param>
    /// <param name="hit">命中</param>
    /// <param name="query">扫出它的关键词：改词之后旧结果还挂着的那一会儿点下去，得按当时的词在会话里找</param>
    public SessionContentHitRow(string sessionId, SessionContentHit hit, string query)
    {
        SessionId = sessionId;
        Reveal = new ConversationSearchReveal(query, hit.MessageIndex);
        Label = ConversationSearchHitRow.LabelOf(hit.Kind, hit.IsUser);
        Snippet = hit.Snippet;
    }

    /// <summary>所在会话</summary>
    public string SessionId { get; }

    /// <summary>点进去要跳的那条</summary>
    public ConversationSearchReveal Reveal { get; }

    /// <summary>来源标签：我 / 回复 / 思考 / 工具</summary>
    public string Label { get; }

    /// <summary>命中前后的一小段</summary>
    public string Snippet { get; }
}
