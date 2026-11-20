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
/// 界面条目与历史的<b>对账</b>：它们的先后顺序还对得上吗、尾部有没有漏画。
///
/// <para>
/// 判据只有一条：<b>条目的稳定身份</b>（见 <see cref="ConversationItemIdentity"/>）。
/// 从前这里问的是「来源消息的对象引用在历史里的下标」，而引用会被冷卸载换掉、
/// 空来源的流式条目压根不参与判定——于是对账只能靠「最大下标做减法」猜漏了几条，
/// 尾部成对时 <c>missing</c> 恒不收敛（真实日志里 5 小时 298 次对账，其中 1576 次是
/// 稳定的「2 trailing message(s) not drawn」）。
/// </para>
/// <para>
/// 现在改成<b>逐条问「这条消息画出来了没有」</b>：漏画就是漏画，画过就是画过，
/// 不再有「缺几条」这个中间量，也就不存在减法算错。
/// </para>
/// </summary>
public static class ConversationOrderCheck
{
    /// <summary>
    /// 找出界面与历史的顺序分歧。
    ///
    /// <para>
    /// 来源已不在历史里的条目也算分歧：它要么是跑着的时候被原地替换掉的旧报告
    /// （替换信号在有轮在跑时不处理，轮结束了就靠这道检查发现），
    /// 要么是历史被压缩或删除变短了——两种都该按当前历史重放，而不是接着往后追加。
    /// </para>
    /// </summary>
    /// <param name="items">界面条目（按显示顺序）</param>
    /// <param name="history">当前历史</param>
    /// <returns>分歧说明（可直接进日志）；一致则为 null</returns>
    public static string? FindDivergence(IReadOnlyList<ConversationItemBase> items,
        IReadOnlyList<ChatMessage> history)
    {
        Dictionary<string, int> order = BuildOrderMap(history);

        int previous = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Identity is not { IsFormal: true } identity) continue;
            // 找不到的是跑着时被替换掉的旧来源，或压缩/删除之后的历史——都是分歧，不再跳过
            if (!order.TryGetValue(identity.MessageId!, out int index))
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
    /// 找出历史尾部<b>第一条没画出来的消息</b>的下标；全都画过返回 -1。
    ///
    /// <para>
    /// 只查尾部：窗口之外的旧消息本来就不画（「加载更早」负责它们），
    /// 只有「最后一条已画消息之后还有历史」才算漏画（实机见过：交回报告与唤醒回复都落盘了，
    /// 界面却停在派活那条，切会话才出来）。
    /// </para>
    /// <para>
    /// 由设计就不产条目的消息（工具结果只回写已有工具卡）跳过——它们永远不会有自己的条目，
    /// 把它们算成漏画就是每个消息边界白跑一次补画。
    /// </para>
    /// </summary>
    /// <param name="items">界面条目（按显示顺序）</param>
    /// <param name="history">当前历史</param>
    /// <returns>补画起始下标；没有漏画为 -1</returns>
    public static int FindMissingTailStart(IReadOnlyList<ConversationItemBase> items,
        IReadOnlyList<ChatMessage> history)
    {
        HashSet<string> rendered = BuildRenderedSet(items);

        // 从尾部往前扫，遇到已画过的就停：再往前是窗口之外的旧消息（「加载更早」负责它们），
        // 把它们算成漏画会让每次对账都从会话开头重画一遍
        int start = -1;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (rendered.Contains(Core.AI.Chat.ChatMessageIdentity.Ensure(history[i]))) break;
            if (ProducesOwnItems(history[i])) start = i;
        }

        return start;
    }

    /// <summary>
    /// 这条历史消息会不会在界面上产生属于自己的条目
    /// </summary>
    /// <param name="message">历史消息</param>
    /// <returns>会产出条目返回 true</returns>
    public static bool ProducesOwnItems(ChatMessage message) =>
        !(message.Role == ChatRole.Tool &&
          ConversationMessageOrigin.KindOf(message) == EHistoryItemKind.StreamContents);

    /// <summary>建「消息标识 → 历史下标」映射。同一条消息重复出现在历史里是另一个 bug，这里只取最后一次</summary>
    private static Dictionary<string, int> BuildOrderMap(IReadOnlyList<ChatMessage> history)
    {
        Dictionary<string, int> order = new(history.Count, System.StringComparer.Ordinal);
        for (int i = 0; i < history.Count; i++)
        {
            order[Core.AI.Chat.ChatMessageIdentity.Ensure(history[i])] = i;
        }

        return order;
    }

    /// <summary>界面上已经画出来的消息标识集合</summary>
    private static HashSet<string> BuildRenderedSet(IReadOnlyList<ConversationItemBase> items)
    {
        HashSet<string> rendered = new(System.StringComparer.Ordinal);
        foreach (ConversationItemBase item in items)
        {
            if (item.Identity is { IsFormal: true } identity) rendered.Add(identity.MessageId!);
        }

        return rendered;
    }
}
