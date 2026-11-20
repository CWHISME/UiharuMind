/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 按种类计数的序号发号器：给「一条消息拆出的若干条目」定 <c>ContentOrdinal</c>。
///
/// <para>
/// 序号必须<b>按种类各数各的</b>：一条消息可以拆出思考卡、正文气泡、若干工具卡，
/// 混在一起数会让「第二张工具卡」与「正文气泡」抢同一个号。按种类数则两边天然对齐——
/// 流式侧一次服务调用恰好产出一条助手消息，历史侧一条消息恰好对应一次服务调用，
/// 于是「段内同类第 N 个」与「消息内同类第 N 个」是同一个 N。
/// </para>
/// </summary>
public sealed class ConversationItemOrdinals
{
    private readonly Dictionary<EConversationItemKind, int> _counts = new();

    /// <summary>取下一个序号并推进（从 0 起）</summary>
    /// <param name="kind">条目种类</param>
    /// <returns>该类已发出的个数</returns>
    public int Next(EConversationItemKind kind)
    {
        _counts.TryGetValue(kind, out int used);
        _counts[kind] = used + 1;
        return used;
    }

    /// <summary>清零（换消息、换段时调）</summary>
    public void Reset() => _counts.Clear();
}
