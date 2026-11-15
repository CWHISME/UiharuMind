/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 补画进会话条目集合的<b>唯一入口</b>：落盘补渲染与对账补尾共用这一道去重闸。
/// 两股来源写同一份集合，只有一边判重时，另一边就会把流已画过的条目再画一份。
/// </summary>
public static class ConversationItemDedup
{
    /// <summary>
    /// 把待补条目追加进集合，界面上已有对应条目的跳过
    /// </summary>
    /// <param name="items">界面条目集合</param>
    /// <param name="fresh">这一批待补的条目</param>
    /// <returns>真正追加的条目数；全被判重挡掉时为 0</returns>
    public static int Append(IList<ConversationItemBase> items, IReadOnlyList<ConversationItemBase> fresh)
    {
        // 已画集合先取快照：边加边判会把同一条消息拆出的第二个条目当成「已经画过」丢掉
        HashSet<ChatMessage> rendered = new(ReferenceEqualityComparer.Instance);
        HashSet<string> calls = new(StringComparer.Ordinal);
        List<ConversationItemBase> unwired = new();
        foreach (ConversationItemBase item in items)
        {
            if (item.SourceMessage is { } drawn) rendered.Add(drawn);
            if (item is ToolCallItem { CallId.Length: > 0 } card) calls.Add(card.CallId);
            if (item.SourceMessage is null) unwired.Add(item);
        }

        // 同一个空来源条目只认领一次：一条消息拆出的思考卡与正文各有自己的对应物
        HashSet<ConversationItemBase> claimed = new(ReferenceEqualityComparer.Instance);
        int added = 0;
        foreach (ConversationItemBase item in fresh)
        {
            // 工具卡认 CallId，不按消息认：一条消息里少一张卡时，按消息判会把它一起判成已画
            if (item is ToolCallItem { CallId.Length: > 0 } freshCall && calls.Contains(freshCall.CallId)) continue;
            if (item.SourceMessage is { } source && rendered.Contains(source) && item is not ToolCallItem) continue;

            // 流已画出但来源还没配对上的条目：按引用判重认不出它，补画就会多出一份。
            // 只认空来源，按引用已配对的条目不参与——正文相同的两条消息仍是两条
            if (FindUnwiredTwin(unwired, claimed, item) is { } twin)
            {
                claimed.Add(twin);
                continue;
            }

            items.Add(item);
            added++;
        }

        return added;
    }

    /// <summary>
    /// 取条目用于认领的内容。思考卡的 <c>Message</c> 是「前 512 字 + 已截断 N 字」的预览，
    /// N 随流式长度逐拍变化，拿它比对必然失配；知识卡则把正文放在 <c>ResultText</c>。
    /// </summary>
    private static string ContentOf(ConversationItemBase item)
    {
        // 加密思考占位卡没有缓冲，正文也不在 Message 上；空缓冲时回落到显示值，
        // 免得改用全文比对后连原本认得出的短卡都认不出
        if (item is ThinkingItem thinking)
        {
            string full = thinking.CurrentText;
            return full.Length > 0 ? full : thinking.Message;
        }

        return item is ToolCallItem { CallId.Length: 0 } card ? card.ResultText : item.Message;
    }

    /// <summary>在还没配对的条目里找与待补条目同源的那一只</summary>
    /// <param name="unwired">界面上来源为空的条目</param>
    /// <param name="claimed">本批已认领过的条目</param>
    /// <param name="item">待补条目</param>
    /// <returns>已画过的同一只条目；没有则为 null</returns>
    private static ConversationItemBase? FindUnwiredTwin(List<ConversationItemBase> unwired,
        HashSet<ConversationItemBase> claimed, ConversationItemBase item)
    {
        // 有 CallId 的工具卡由 CallId 判；无 CallId 的（知识卡）按结果文本认领。
        // 其余卡片不参与内容认领：形状判断交给类别，别在这里猜
        if (item is not TextConversationItem && item is not ThinkingItem &&
            item is not ToolCallItem { CallId.Length: 0 }) return null;

        string text = ContentOf(item).Trim();
        if (text.Length == 0) return null;

        foreach (ConversationItemBase candidate in unwired)
        {
            if (claimed.Contains(candidate)) continue;
            if (candidate.GetType() != item.GetType()) continue;
            if (candidate.IsUser != item.IsUser) continue;
            // 长思考先比全文长度，省掉几十万字的 ToString；有一方没缓冲时交给内容比对
            if (item is ThinkingItem freshThinking && candidate is ThinkingItem drawnThinking &&
                freshThinking.FullLength > 0 && drawnThinking.FullLength > 0 &&
                freshThinking.FullLength != drawnThinking.FullLength) continue;
            if (string.Equals(ContentOf(candidate).Trim(), text, StringComparison.Ordinal)) return candidate;
        }

        return null;
    }
}
