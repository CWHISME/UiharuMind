/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 编辑历史时「必须一起动」的消息范围。
///
/// 工具调用与它的结果是一对不可分割的消息：只删掉其中一头，下一次请求就会因悬空的
/// tool_call / tool_result 被服务端拒掉（400）。
///
/// <b>为什么必须按 CallId 算，而不能按消息位置或内容形状猜</b>：本仓的 assistant 消息
/// 常见形态是「思考 + 正文 + 工具调用」同处一条（见 <c>ChatContentNormalizer</c>），
/// 因此「这条消息带正文，说明它是一次完整回复、可以当作段边界」这类判据一律不成立——
/// 那条消息的结果还在后面等着。同理，向前扫时越过一条工具结果消息也总是错的：
/// 结果永远属于它<b>前面</b>那次调用，不可能是后面某条回复的处理过程。
///
/// 这里只回答「哪些消息必须一起动」，不掺任何界面概念，因此可以被删除、分叉、
/// 以及将来的历史裁剪共用。
/// </summary>
public static class HistoryEditRange
{
    /// <summary>
    /// 删除某条消息时必须一并删除的全部消息（含它自己）。
    ///
    /// 结果对「调用 ↔ 结果」配对闭合：沿 CallId 把相连的调用消息与结果消息全部拉进来，
    /// 直到不再增长。因此删除之后绝不会剩下任何一头。并行调用（一条消息里多个
    /// <see cref="FunctionCallContent"/>）也一并闭合——留下半组结果同样会被拒。
    /// </summary>
    /// <param name="history">会话历史</param>
    /// <param name="target">要删除的消息</param>
    /// <returns>必须删除的消息，按历史顺序；<paramref name="target"/> 不在历史里时为空</returns>
    public static IReadOnlyList<ChatMessage> ResolveDeletion(IReadOnlyList<ChatMessage> history, ChatMessage target)
    {
        int index = IndexOf(history, target);
        if (index < 0) return [];

        // 一个 CallId 上挂着它的调用消息与结果消息,顺着它做连通分量遍历
        Dictionary<string, List<int>> byCallId = new();
        for (int i = 0; i < history.Count; i++)
        {
            foreach (string callId in CallIdsOf(history[i]))
            {
                if (!byCallId.TryGetValue(callId, out List<int>? owners)) byCallId[callId] = owners = [];
                if (!owners.Contains(i)) owners.Add(i);
            }
        }

        HashSet<int> selected = [index];
        Queue<int> pending = new();
        pending.Enqueue(index);

        while (pending.Count > 0)
        {
            int current = pending.Dequeue();
            foreach (string callId in CallIdsOf(history[current]))
            {
                if (!byCallId.TryGetValue(callId, out List<int>? owners)) continue;
                foreach (int owner in owners)
                {
                    if (selected.Add(owner)) pending.Enqueue(owner);
                }
            }
        }

        return selected.Order().Select(i => history[i]).ToList();
    }

    /// <summary>
    /// 把「只保留前 <paramref name="keepCount"/> 条」的截断点后移到不留悬空调用的位置。
    ///
    /// 分叉与重置都是截断尾部，而截断点很容易正好落在一条「正文 + 工具调用」消息之后、
    /// 它的结果之前——那样分出去的会话一开口就是 400。把边界推到所有待答调用都拿到
    /// 结果为止；新纳入的消息若又带来调用，继续往后推。
    /// </summary>
    /// <param name="history">会话历史</param>
    /// <param name="keepCount">原本打算保留的条数</param>
    /// <returns>修正后的保留条数（不小于 <paramref name="keepCount"/>）</returns>
    public static int ExpandKeptPrefix(IReadOnlyList<ChatMessage> history, int keepCount)
    {
        if (keepCount <= 0) return 0;
        if (keepCount >= history.Count) return history.Count;

        HashSet<string> unanswered = [];
        for (int i = 0; i < keepCount; i++) Settle(history[i], unanswered);

        int end = keepCount;
        while (unanswered.Count > 0 && end < history.Count)
        {
            Settle(history[end], unanswered);
            end++;
        }

        return end;
    }

    /// <summary>把一条消息记入待答集合：它的结果消掉待答，它的调用加入待答</summary>
    private static void Settle(ChatMessage message, HashSet<string> unanswered)
    {
        foreach (AIContent content in message.Contents)
        {
            switch (content)
            {
                case FunctionResultContent result:
                    unanswered.Remove(result.CallId);
                    break;
                case FunctionCallContent call:
                    unanswered.Add(call.CallId);
                    break;
            }
        }
    }

    /// <summary>这条消息牵涉到的全部 CallId（调用与结果同等看待——两头都是配对的一半）</summary>
    private static IEnumerable<string> CallIdsOf(ChatMessage message)
    {
        foreach (AIContent content in message.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call when !string.IsNullOrEmpty(call.CallId):
                    yield return call.CallId;
                    break;
                case FunctionResultContent result when !string.IsNullOrEmpty(result.CallId):
                    yield return result.CallId;
                    break;
            }
        }
    }

    /// <summary>按引用定位:ChatMessage 不重写相等性,内容相同的两条消息必须区分开</summary>
    private static int IndexOf(IReadOnlyList<ChatMessage> history, ChatMessage target)
    {
        for (int i = 0; i < history.Count; i++)
        {
            if (ReferenceEquals(history[i], target)) return i;
        }

        return -1;
    }
}
