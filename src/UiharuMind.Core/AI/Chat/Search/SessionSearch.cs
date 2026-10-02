/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.AI.Chat.Search;

/// <summary>命中落在消息的哪一部分</summary>
public enum ESearchHitKind
{
    /// <summary>正文：用户说的、回复、交接文档、后续报告、旁白</summary>
    Text,

    /// <summary>思考过程</summary>
    Thinking,

    /// <summary>工具调用的参数与结果、知识库检索片段</summary>
    Tool,
}

/// <summary>搜索范围。默认只搜正文：思考与工具结果又长又多，全搜命中多半是噪音</summary>
/// <param name="IncludeThinking">连思考一起搜</param>
/// <param name="IncludeTools">连工具调用与检索片段一起搜</param>
public readonly record struct SessionSearchOptions(bool IncludeThinking = false, bool IncludeTools = false);

/// <summary>一条命中。一条消息至多一条，定位粒度是卡片</summary>
/// <param name="MessageIndex">搜索那一刻在历史里的下标（历史之后可能变，定位以 <paramref name="Message"/> 为准）</param>
/// <param name="Message">命中的消息</param>
/// <param name="Kind">落在哪一部分（同一条消息里正文优先，其次思考、工具）</param>
/// <param name="Snippet">命中前后的一小段，空白已折叠</param>
public sealed record SessionSearchHit(int MessageIndex, ChatMessage Message, ESearchHitKind Kind, string Snippet);

/// <summary>
/// 会话历史的全文搜索。搜的是<b>消息历史</b>而不是界面条目——会话流按窗渲染、滚远的卡片会被卸掉、
/// 长思考与工具结果只显示截断预览，界面上的东西从来不全（见 ADR 0056）。
///
/// 纯函数、不碰存储：单个会话直接喂历史；跨会话搜索（<see cref="SessionContentSearch"/>）逐行解析后逐条喂 <see cref="FindIn"/>，同一套口径
/// </summary>
public static class SessionSearch
{
    private const int SnippetBefore = 20;
    private const int SnippetAfter = 60;

    /// <summary>
    /// 在一份历史里找关键词（不区分大小写），按历史顺序返回
    /// </summary>
    /// <param name="history">历史快照（调用方先复制一份再交给后台线程，别直接传会话本体那一份）</param>
    /// <param name="query">关键词；空白返回空</param>
    /// <param name="options">搜索范围</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>命中，按历史顺序</returns>
    public static List<SessionSearchHit> Find(IReadOnlyList<ChatMessage> history, string? query,
        SessionSearchOptions options = default, CancellationToken cancellationToken = default)
    {
        List<SessionSearchHit> hits = new();
        if (KeywordOf(query) is not { } keyword) return hits;

        for (int i = 0; i < history.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FindIn(history[i], i, keyword, options) is { } hit) hits.Add(hit);
        }

        return hits;
    }

    /// <summary>关键词规整：去首尾空白，空白返回 null</summary>
    internal static string? KeywordOf(string? query) => query?.Trim() is { Length: > 0 } keyword ? keyword : null;

    /// <summary>一条消息里的命中（跨会话扫描逐行解析，下标由调用方按行号给）</summary>
    internal static SessionSearchHit? FindIn(ChatMessage message, int index, string keyword,
        SessionSearchOptions options) =>
        Match(message, keyword, options) is { } match
            ? new SessionSearchHit(index, message, match.Kind, match.Snippet)
            : null;

    private static (ESearchHitKind Kind, string Snippet)? Match(ChatMessage message, string keyword,
        SessionSearchOptions options)
    {
        if (ChatMessageDisplay.IsFrameworkInjected(message)) return null; //与界面同一口径:不画的不搜

        MessageText text = Extract(message, options);
        foreach ((ESearchHitKind kind, string? content) in new[]
                 {
                     (ESearchHitKind.Text, text.Body),
                     (ESearchHitKind.Thinking, text.Thinking),
                     (ESearchHitKind.Tool, text.Tool),
                 })
        {
            if (string.IsNullOrEmpty(content)) continue;
            int at = content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) return (kind, SnippetOf(content, at, keyword.Length));
        }

        return null;
    }

    /// <summary>按界面显示的口径把一条消息拆成三部分。不搜的部分不拼，长工具结果不白拷一份</summary>
    private static MessageText Extract(ChatMessage message, SessionSearchOptions options)
    {
        // 检索片段画成工具卡,归工具那一档
        if (ChatMessageAnnotations.IsKnowledge(message))
            return new MessageText(null, null, options.IncludeTools ? message.Text : null);

        // 这几类整条就是一张卡,正文即全部
        if (HistoryHandoff.IsNote(message))
            return new MessageText(HistoryHandoff.NoteBody(message.Text), null, null);
        if (ChatMessageAnnotations.IsNarration(message) || ChatMessageAnnotations.IsSubAgentReport(message) ||
            ChatMessageAnnotations.GroupAwayReceiptOf(message) != null)
            return new MessageText(message.Text, null, null);

        if (message.Role == ChatRole.User) return new MessageText(ChatMessageDisplay.TextOf(message), null, null);
        // 交接文档的角色也是 system(见 HistoryHandoff.CreateNote),所以排在认完卡片之后
        if (message.Role == ChatRole.System) return default;

        StringBuilder body = new();
        StringBuilder? thinking = options.IncludeThinking ? new StringBuilder() : null;
        StringBuilder? tool = options.IncludeTools ? new StringBuilder() : null;
        ThinkTagStreamParser? parser = null;
        foreach (AIContent content in message.Contents)
        {
            switch (content)
            {
                case TextReasoningContent reasoning:
                    thinking?.Append(reasoning.Text).Append('\n');
                    break;
                case TextContent { Text.Length: > 0 } plain:
                    // 本地/部分远程模型把 <think> 混在正文里,界面上那段画成思考卡
                    parser ??= new ThinkTagStreamParser();
                    parser.Feed(plain.Text, x => body.Append(x), x => thinking?.Append(x));
                    break;
                case FunctionCallContent call when tool != null:
                    tool.Append(call.Name).Append(' ');
                    if (call.Arguments != null)
                    {
                        foreach (KeyValuePair<string, object?> argument in call.Arguments)
                            tool.Append(argument.Key).Append(": ").Append(argument.Value).Append('\n');
                    }

                    break;
                case FunctionResultContent result when tool != null:
                    tool.Append(result.Result).Append('\n');
                    break;
            }
        }

        parser?.Complete(x => body.Append(x), x => thinking?.Append(x));
        return new MessageText(body.ToString(), thinking?.ToString(), tool?.ToString());
    }

    /// <summary>命中前后各取一小段，换行与连续空白折成一个空格（结果列表一行一条）</summary>
    internal static string SnippetOf(string text, int at, int length)
    {
        int from = Math.Max(0, at - SnippetBefore);
        int to = Math.Min(text.Length, at + length + SnippetAfter);
        StringBuilder sb = new(to - from + 2);
        if (from > 0) sb.Append('…');
        bool lastWasSpace = false;
        for (int i = from; i < to; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && sb.Length > 0) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        if (to < text.Length) sb.Append('…');
        return sb.ToString().Trim();
    }

    private readonly record struct MessageText(string? Body, string? Thinking, string? Tool);
}
