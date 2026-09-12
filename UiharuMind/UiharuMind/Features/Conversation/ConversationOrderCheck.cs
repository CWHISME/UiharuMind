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
/// 判据只有一条：<b>已配对的条目，其来源消息在历史里的下标必须单调不减</b>。
/// 不查"有没有漏画"——尚未落盘的气泡（实时画出的那些）的来源还不在历史里，
/// 把它当成缺失会在每轮结尾误判一次。
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

    /// <summary>
    /// 找出界面与历史的顺序分歧
    /// </summary>
    /// <param name="items">界面条目（按显示顺序）</param>
    /// <param name="history">当前历史</param>
    /// <returns>分歧说明（可直接进日志）；一致则为 null</returns>
    public static string? FindDivergence(IReadOnlyList<ConversationItemBase> items,
        IReadOnlyList<ChatMessage> history)
    {
        Dictionary<ChatMessage, int> order = new(history.Count, MessageIdentity.Instance);
        for (int i = 0; i < history.Count; i++)
        {
            order[history[i]] = i; //同一条消息重复出现在历史里是另一个 bug,这里只取最后一次
        }

        int previous = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].SourceMessage is not { } source) continue;
            // 找不到的是窗口之外或已被删掉的来源,不参与判定
            if (!order.TryGetValue(source, out int index)) continue;

            if (index < previous)
            {
                return $"item #{i} points at history #{index}, which is behind #{previous}";
            }

            previous = index;
        }

        return null;
    }
}
