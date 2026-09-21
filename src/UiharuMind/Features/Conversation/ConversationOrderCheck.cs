/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 界面条目与历史的<b>对账</b>：它们的先后顺序还对得上吗。
///
/// 存在的理由：一轮跑着的时候，条目来自两处——实时内容流（助手正文、思考段、工具卡、
/// 被消费的用户消息）与历史落盘（检索卡、后续报告…）。边界与用户消息如今由执行者在流里
/// 明说，不再靠界面推演；但两股来源仍然存在，所以留一道事后校验：<b>错了要能自己发现</b>，
/// 由调用方重放那一窗纠正，并在日志里留痕（不然下次还是靠用户截图才知道）。
///
/// 判据有两条：
/// 1. <b>已配对的条目，其来源消息在历史里的下标必须单调不减</b>（<see cref="FindDivergence"/>）。
/// 2. <b>历史尾部没有漏画</b>（<see cref="FindMissingTail"/>）。
///
/// 漏画只查尾部：「加载更早」负责窗口之外更早的那段，只有最后一条已画消息之后还有历史
/// 才算漏。调用方必须保证<b>没人跑</b>——流式中的气泡来源是空，跑着的时候查必误判。
/// </summary>
public static class ConversationOrderCheck
{
    /// <summary>消息按<b>引用</b>认人：正文相同的两条消息是两条,不能合并</summary>
    private sealed class MessageIdentity : IEqualityComparer<ChatMessage>
    {
        public static readonly MessageIdentity Instance = new();

        public bool Equals(ChatMessage? x, ChatMessage? y) => ReferenceEquals(x, y);

        public int GetHashCode(ChatMessage obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>建「消息 → 历史下标」映射。同一条消息重复出现在历史里是另一个 bug，这里只取最后一次</summary>
    private static Dictionary<ChatMessage, int> BuildOrderMap(IReadOnlyList<ChatMessage> history)
    {
        Dictionary<ChatMessage, int> order = new(history.Count, MessageIdentity.Instance);
        for (int i = 0; i < history.Count; i++)
        {
            order[history[i]] = i;
        }

        return order;
    }

/// <summary>
/// 找出界面与历史的顺序分歧
///
/// 来源已不在历史里的条目也算分歧：它要么是跑着的时候被原地替换掉的旧报告
/// （替换信号在有轮在跑时不处理，轮结束了就靠这道检查发现），
/// 要么是历史被压缩或删除变短了——两种都该按当前历史重放，而不是接着往后追加。
/// 尚未配对的气泡（来源是空）不参与判定，否则每轮直播都要误判一次。
/// </summary>
    /// <param name="items">界面条目（按显示顺序）</param>
    /// <param name="history">当前历史</param>
    /// <returns>分歧说明（可直接进日志）；一致则为 null</returns>
    public static string? FindDivergence(IReadOnlyList<ConversationItemBase> items,
        IReadOnlyList<ChatMessage> history)
    {
        Dictionary<ChatMessage, int> order = BuildOrderMap(history);

        int previous = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].SourceMessage is not { } source) continue;
            // 找不到的是跑着时被替换掉的旧来源，或压缩/删除之后的历史——都是分歧，不再跳过
            if (!order.TryGetValue(source, out int index))
            {
                return $"item #{i} source no longer in history (replaced or removed)";
            }

            if (index < previous)
            {
                return $"item #{i} points at history #{index}, which is behind #{previous}";
            }

            previous = index;
        }

        return null;
    }

    /// <summary>
    /// 找出历史尾部没画出来的条数（有追加落盘、界面一条没加）。
    ///
    /// 只查尾部：窗口之外的旧消息本来就不画（「加载更早」负责它们），
    /// 只有“最后一条已画消息之后还有历史”才是漏画（实机见过：交回报告与唤醒回复都落盘了，
    /// 界面却停在派活那条，切会话才出来）。
    ///
    /// 调用方必须保证没人跑——流式中的气泡来源是空，跑着的时候查这个必误判。
    /// </summary>
    /// <param name="items">界面条目（按显示顺序）</param>
    /// <param name="history">当前历史</param>
    /// <returns>尾部漏画的消息数；0 为对得上</returns>
    public static int FindMissingTail(IReadOnlyList<ConversationItemBase> items,
        IReadOnlyList<ChatMessage> history)
    {
        Dictionary<ChatMessage, int> order = BuildOrderMap(history);

        int lastDrawn = -1;
        foreach (ConversationItemBase item in items)
        {
            if (item.SourceMessage is not { } source) continue;
            if (order.TryGetValue(source, out int index) && index > lastDrawn) lastDrawn = index;
        }

        return history.Count - 1 - lastDrawn;
    }
}
