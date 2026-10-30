using System.Text;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Reasoning;

/// <summary>
/// 一次请求的回填计划：<b>一处</b>定义「第几条助手消息」这个约定，并顺带产出要发出去的那份消息。
///
/// <para><b>为什么需要这个类</b>：回填按序索引，前提是两侧用同一个顺序数助手消息。
/// 早先这个约定抄在两处——收集明文思考一遍、收集票据一遍，各自 <c>foreach</c> 加自己的
/// <c>index++</c>，谁漏一个 <c>continue</c> 的边界条件就静默错位，而错位不会换来 400，
/// 只会让服务端把思考配到别的消息上。现在只有一次遍历、一个有序列表，
/// <b>序号就是列表下标</b>，抄不出第二份。</para>
///
/// <para><b>为什么不能「直接挂在消息上」</b>：chat 线的思考要写进 wire 的
/// <c>reasoning_content</c> / <c>reasoning_details</c>，而这两个字段在
/// <c>OpenAI.Chat.ChatMessage</c> 上没有位置（2.13 只暴露 <c>Content</c>，
/// 也没有 <c>SerializedAdditionalRawData</c>），<c>Microsoft.Extensions.AI.OpenAI</c>
/// 又不提供 ChatMessage→wire 的转换器注入点——它会把 <see cref="TextReasoningContent"/>
/// 直接丢掉。所以只能等 SDK 序列化完，在字节层补回去；而字节层拿不到消息对象，
/// 只能靠序号对齐。这是 SDK 的限制，不是设计偏好。</para>
/// </summary>
internal sealed class ReplayPlan
{
    private readonly List<string?>? _reasoning; //按助手消息顺序;整列全空时为 null
    private readonly List<string?>? _tickets; //同上,只放真正带加密条目的票

    private ReplayPlan(IReadOnlyList<ChatMessage> messages, List<string?>? reasoning, List<string?>? tickets)
    {
        Messages = messages;
        _reasoning = reasoning;
        _tickets = tickets;
    }

    /// <summary>要发给底层客户端的那份（门禁不过的票已摘掉，历史里的原消息不动）</summary>
    public IReadOnlyList<ChatMessage> Messages { get; }

    /// <summary>按助手消息顺序的思考正文；一条都没有时为 null（让改写整段跳过）</summary>
    public IReadOnlyList<string?>? Reasoning => _reasoning;

    /// <summary>按助手消息顺序的加密票据；一条都没有时为 null</summary>
    public IReadOnlyList<string?>? Tickets => _tickets;

    /// <summary>
    /// 按门禁与历史造一份回填计划。
    /// </summary>
    /// <param name="messages">历史消息</param>
    /// <param name="current">本次请求的模型身份</param>
    /// <returns>回填计划</returns>
    public static ReplayPlan For(IReadOnlyList<ChatMessage> messages, ReasoningGate current)
    {
        // 先冻结：历史是可变集合（消息支持随时增删改），而回填靠序号对齐——
        // 走一遍的第 N 条助手消息，必须就是序列化出去的第 N 条。
        // 增删会让条数变化，由改写器的数量闸门兜住；改一条（重新生成、编辑）条数不变，
        // 闸门拦不住，只有冻结能保证两侧看到同一份
        ChatMessage[] snapshot = [.. messages];

        // 门禁摘票：摘掉的是发给底层客户端的那一份，历史里的原票不动（换回原模型还能接上）
        IReadOnlyList<ChatMessage> gated = ReasoningGateFilter.Apply(snapshot, current);

        List<string?>? reasoning = null;
        List<string?>? tickets = null;
        foreach (ChatMessage message in gated)
        {
            if (message.Role != ChatRole.Assistant) continue;

            (reasoning ??= []).Add(JoinReasoning(message.Contents));
            (tickets ??= []).Add(ReadTicket(message));
        }

        return new ReplayPlan(gated, AllNull(reasoning) ? null : reasoning, AllNull(tickets) ? null : tickets);
    }

    /// <summary>
    /// 一条消息上的票据。门禁已经在前面过了一遍，这里只管取——
    /// 再比一次 <see cref="ReasoningGate.Allows"/> 是死条件，只会让「门禁在哪」多一个说法。
    /// </summary>
    /// <param name="message">已过门禁的消息</param>
    /// <returns>票据原文；没有时为 null</returns>
    private static string? ReadTicket(ChatMessage message) =>
        ReasoningTicketCarrier.Read(message) is { } ticket ? ticket.Json : null;

    /// <summary>
    /// 拼齐一条消息里的多段思考；没有思考正文时返回 null。
    ///
    /// 老会话的思考是逐 chunk 落下的碎片，要拼齐了发回去——只取其中一段等于把思考截成一句话，
    /// 接口那边照样对不上。只有一段时直接用它的引用，不另拼一份。
    /// </summary>
    /// <param name="contents">消息内容</param>
    /// <returns>拼好的思考正文；没有时为 null</returns>
    private static string? JoinReasoning(IList<AIContent> contents)
    {
        string? single = null;
        StringBuilder? joined = null;
        foreach (AIContent content in contents)
        {
            switch (content)
            {
                case TextReasoningContent { Text.Length: > 0 } rc when single == null:
                    single = rc.Text;
                    break;
                case TextReasoningContent { Text.Length: > 0 } rc:
                    (joined ??= new StringBuilder(single)).Append(rc.Text);
                    break;
            }
        }

        return single == null ? null : joined?.ToString() ?? single;
    }

    private static bool AllNull(List<string?>? values) => values == null || !values.Exists(x => x != null);
}
