namespace UiharuMind.Core.AI.Reasoning;

/// <summary>
/// 一张票据：原文加上产出它的模型身份。
///
/// 身份跟着票走，不是跟着 store 走——同一个会话里换过模型，两边的票都得留住，
/// 换回来还能接着用。
/// </summary>
/// <param name="Json">票据原文（整个 <c>reasoning_details</c> 数组）</param>
/// <param name="Producer">产出它的供应商与模型</param>
internal readonly record struct ReasoningTicket(string Json, ReasoningGate Producer);

/// <summary>
/// 按「助手消息序号 → 票据」存加密思考凭证。
///
/// <b>每条助手消息挂自己那张票，挂上就不动。</b>
/// 这是缓存前缀稳定的前提：一条消息的票据字节从产生那刻起不再变，
/// 每轮请求只是把它原样重发，历史部分逐字节相同 → 前缀是纯追加。
///
/// 上一版把票据存在<b>单个槽位</b>里、只挂最后一条助手消息、用一次即消费，
/// 于是票逐轮往新位置跑，上一轮挂过票的那条变成无票形态 → 前缀每轮分叉一次
/// （实测命中率从 88.2% 掉到 35.3%）。这里用「按序号存」代替「单槽位」，
/// 用「永不挪动」代替「用一次即消费」。
///
/// <b>第一版是内存版</b>：进程退出即丢，重启后历史里挂过票的消息变回无票形态，
/// 那一条起同样全冷。跨重启要等落盘（见 ADR 0100 的已知代价）。
/// </summary>
internal sealed class TicketStore
{
    private readonly Dictionary<int, ReasoningTicket> _tickets = [];

    /// <summary>已记下票据的助手消息条数</summary>
    public int Count => _tickets.Count;

    /// <summary>
    /// 记下一条助手消息产出的票据。
    /// </summary>
    /// <param name="assistantIndex">该助手消息在历史里的序号（按出现顺序，0 起）</param>
    /// <param name="json">票据原文（整个 <c>reasoning_details</c> 数组）</param>
    /// <param name="producer">产出它的供应商与模型</param>
    public void Record(int assistantIndex, string json, ReasoningGate producer)
    {
        if (assistantIndex < 0) return;
        _tickets[assistantIndex] = new ReasoningTicket(json, producer);
    }

    /// <summary>
    /// 取第几条助手消息的票据，不做门禁。
    /// </summary>
    /// <param name="assistantIndex">助手消息序号</param>
    /// <returns>票据原文；那一条没有票时为 null</returns>
    public string? TicketAt(int assistantIndex) =>
        _tickets.TryGetValue(assistantIndex, out ReasoningTicket ticket) ? ticket.Json : null;

    /// <summary>
    /// 取第几条助手消息的票据，门禁不过就不给。
    ///
    /// 门禁不过时<b>不删票</b>：换回原来的模型还要接着用。
    /// </summary>
    /// <param name="assistantIndex">助手消息序号</param>
    /// <param name="current">本次请求的模型身份</param>
    /// <returns>票据原文；没有票或门禁不过时为 null</returns>
    public string? TicketAt(int assistantIndex, ReasoningGate current)
    {
        if (!_tickets.TryGetValue(assistantIndex, out ReasoningTicket ticket)) return null;
        return ticket.Producer.Allows(current.Provider, current.Model) ? ticket.Json : null;
    }
}
