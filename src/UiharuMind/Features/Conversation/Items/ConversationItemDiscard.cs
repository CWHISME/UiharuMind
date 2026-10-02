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

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 永久丢弃条目：先从集合里摘掉，再释放它们气泡里的大位图。
/// 顺序不能反——还挂在界面上的位图一释放，下一帧渲染就撞上去（见 <see cref="ConversationItemBase.ReleaseImages"/>）；
/// 只摘不放又会让切一次会话就漏掉一整个会话的图。切会话、整窗重放、裁剪、删除、重试截断都走这里
/// </summary>
public static class ConversationItemDiscard
{
    /// <summary>丢弃全部条目</summary>
    /// <param name="items">条目集合</param>
    public static void DiscardAll(this IList<ConversationItemBase> items)
    {
        ConversationItemBase[] discarded = items.ToArray();
        items.Clear();
        Release(discarded);
    }

    /// <summary>丢弃一段连续的条目</summary>
    /// <param name="items">条目集合</param>
    /// <param name="from">起始下标</param>
    /// <param name="count">条数</param>
    public static void DiscardRange(this IList<ConversationItemBase> items, int from, int count)
    {
        List<ConversationItemBase> discarded = new(count);
        for (int i = from + count - 1; i >= from; i--)
        {
            discarded.Add(items[i]);
            items.RemoveAt(i);
        }

        Release(discarded);
    }

    /// <summary>丢弃指定的那些条目（不在集合里的只释放）</summary>
    /// <param name="items">条目集合</param>
    /// <param name="targets">要丢弃的条目</param>
    public static void Discard(this IList<ConversationItemBase> items, IReadOnlyCollection<ConversationItemBase> targets)
    {
        HashSet<ConversationItemBase> doomed = new(targets);
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (doomed.Contains(items[i])) items.RemoveAt(i);
        }

        Release(targets);
    }

    private static void Release(IEnumerable<ConversationItemBase> discarded)
    {
        foreach (ConversationItemBase item in discarded) item.ReleaseImages();
    }
}
