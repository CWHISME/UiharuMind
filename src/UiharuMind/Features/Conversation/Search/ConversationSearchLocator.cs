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
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Search;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation.Search;

/// <summary>
/// 命中 → 界面上那张卡。按条目的来源消息认（与删除、对账同一口径），纯数据、不碰视图
/// </summary>
public static class ConversationSearchLocator
{
    /// <summary>
    /// 命中消息在当前历史里的下标。搜索之后历史可能被追加、删除或压缩，按实例重认，不信快照里的下标
    /// </summary>
    /// <param name="history">当前历史</param>
    /// <param name="hit">命中</param>
    /// <returns>下标；已不在历史里为 -1</returns>
    public static int IndexOf(IReadOnlyList<ChatMessage> history, SessionSearchHit hit)
    {
        if (hit.MessageIndex < history.Count && ReferenceEquals(history[hit.MessageIndex], hit.Message))
            return hit.MessageIndex;

        for (int i = 0; i < history.Count; i++)
        {
            if (ReferenceEquals(history[i], hit.Message)) return i;
        }

        return -1;
    }

    /// <summary>
    /// 找命中对应的卡片：优先同一条消息里同一部分的那张（命中在思考就落到思考卡上），
    /// 这条消息没有自己的卡（工具结果并进了调用那张卡）就往前找最近一条有卡的
    /// </summary>
    /// <param name="items">界面条目</param>
    /// <param name="history">当前历史</param>
    /// <param name="hit">命中</param>
    /// <returns>卡片；还没画出来为 null</returns>
    public static ConversationItemBase? Find(IReadOnlyList<ConversationItemBase> items,
        IReadOnlyList<ChatMessage> history, SessionSearchHit hit)
    {
        int index = IndexOf(history, hit);
        if (index < 0) return null;

        Dictionary<ChatMessage, List<ConversationItemBase>> bySource = new(ReferenceEqualityComparer.Instance);
        foreach (ConversationItemBase item in items)
        {
            if (item.SourceMessage is not { } source) continue;
            if (!bySource.TryGetValue(source, out List<ConversationItemBase>? list))
                bySource[source] = list = new List<ConversationItemBase>();
            list.Add(item);
        }

        for (int i = index; i >= 0; i--)
        {
            if (!bySource.TryGetValue(history[i], out List<ConversationItemBase>? candidates)) continue;
            return candidates.FirstOrDefault(x => Fits(x, hit.Kind)) ?? candidates[0];
        }

        return null;
    }

    /// <summary>把命中所在的折叠卡展开，否则跳过去只看得到一行标题</summary>
    /// <param name="item">卡片</param>
    /// <param name="kind">命中部分</param>
    public static void Reveal(ConversationItemBase item, ESearchHitKind kind)
    {
        switch (item)
        {
            case ThinkingItem thinking when kind == ESearchHitKind.Thinking:
                thinking.IsExpanded = true;
                break;
            case ToolCallItem tool when kind == ESearchHitKind.Tool:
                tool.IsExpanded = true;
                break;
            case HandoffItem handoff:
                handoff.IsExpanded = true;
                break;
        }
    }

    private static bool Fits(ConversationItemBase item, ESearchHitKind kind) => kind switch
    {
        ESearchHitKind.Thinking => item is ThinkingItem,
        ESearchHitKind.Tool => item is ToolCallItem,
        _ => item is TextConversationItem or HandoffItem,
    };
}
