using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Reasoning;

/// <summary>
/// 票据的门禁过滤：把<b>不该发给当前模型</b>的那些票，从发给底层客户端的那份消息里摘掉。
///
/// <para><b>为什么要有这一层</b>：票据是「只有原厂认得的凭证」，换模型就没有意义——
/// 而<b>网关自己不校验</b>（实测把某模型的票发给另一个模型照样 200）。也就是说发错了不会报错，
/// 只会安静地错：模型拿到一段读不懂的密文，轻则忽略（白花钱），重则误解上下文。
/// 所以这道门禁只能在我们这一侧，且必须按 provider + model 双等比对
/// （同一个 model id 在不同供应商下是不同的东西，票是原厂签的，跨供应商同样不认）。
///
/// <para><b>为什么不能只靠 chat 线的收集侧</b>：早先那道门禁长在
/// <c>LazyChatClient.CollectTickets</c> 里——它把票据按助手消息序号收集起来，
/// 交给 chat 线的字节级改写器拼回请求体。那是<b>chat 线专用</b>的路子：
/// responses 线与 messages 线的密文是 <b>SDK 自己从消息里读</b>的
/// （<c>ProtectedData</c> → <c>encrypted_content</c> / <c>thinking.signature</c>），
/// 不经过那道收集——于是换模型后旧模型的票会被一起发出去，门禁形同虚设。
///
/// <para><b>这一层怎么补</b>：在把消息交给底层客户端<b>之前</b>，扫一遍历史里带票据的思考内容，
/// 门禁不过就把它的密文摘掉。摘掉的是<b>发给底层客户端的那一份</b>——
/// 历史里的原消息一个字节都不动，所以换回原模型时票还在、还能接上
/// （ADR 0100：门禁不过时不能删票，只是这一轮不带）。
///
/// <para><b>零克隆是常态</b>：门禁通过时原样返回入参那一份。
/// 只有真有票要摘时才重建受影响的那几条消息，其余消息复用原对象——
/// 同模型连续对话（绝大多数情况）一次对象都不分配。
/// </para>
/// </summary>
internal static class ReasoningGateFilter
{
    /// <summary>
    /// 按门禁过滤一批消息里的票据。
    /// </summary>
    /// <param name="messages">历史消息</param>
    /// <param name="current">本次请求的模型身份</param>
    /// <returns>
    /// 过滤后的消息；<b>没有要摘的票时与入参同一份</b>（零克隆）。
    /// </returns>
    public static IReadOnlyList<ChatMessage> Apply(IReadOnlyList<ChatMessage> messages, ReasoningGate current)
    {
        // 先扫一遍：确认真有要摘的再动手。绝大多数情况（同一模型连续对话）这里就走完了
        bool any = false;
        foreach (ChatMessage message in messages)
        {
            if (!HasBlockedTicket(message, current)) continue;
            any = true;
            break;
        }

        if (!any) return messages;

        List<ChatMessage> gated = new(messages.Count);
        foreach (ChatMessage message in messages)
        {
            gated.Add(GateMessage(message, current));
        }

        return gated;
    }

    /// <summary>这条消息里有没有「门禁不过、这一轮不能带」的票</summary>
    private static bool HasBlockedTicket(ChatMessage message, ReasoningGate current)
    {
        foreach (AIContent content in message.Contents)
        {
            if (content is not TextReasoningContent { ProtectedData.Length: > 0 } reasoning) continue;
            if (IsBlocked(reasoning, current)) return true;
        }

        return false;
    }

    /// <summary>
    /// 这张票这一轮能不能带。
    ///
    /// <b>身份读不出来也算不能带</b>：门禁无从判断，宁可不发——
    /// 与 <see cref="ReasoningTicketCarrier.Read"/> 同一个口径。
    /// </summary>
    private static bool IsBlocked(TextReasoningContent reasoning, ReasoningGate current) =>
        !ReasoningTicketCarrier.TryReadProducer(reasoning, out ReasoningGate producer)
        || !producer.Allows(current.Provider, current.Model);

    /// <summary>
    /// 摘掉一条消息里门禁不过的票。要摘才克隆——原消息不动，历史里的票还在。
    /// </summary>
    private static ChatMessage GateMessage(ChatMessage message, ReasoningGate current)
    {
        List<AIContent>? contents = null;
        // 倒序遍历：删元素不用挪索引
        for (int i = message.Contents.Count - 1; i >= 0; i--)
        {
            AIContent content = message.Contents[i];
            if (content is not TextReasoningContent { ProtectedData.Length: > 0 } reasoning) continue;
            if (!IsBlocked(reasoning, current)) continue;

            contents ??= [.. message.Contents];
            if (reasoning.Text.Length == 0)
            {
                // 正文也没有：摘掉密文后就是一条空内容——丢掉。
                // 留着的话各协议会把它当成一个「空思考块」发出去
                // （responses 侧是一条没有 encrypted_content 的 reasoning item，
                //  Anthropic 侧是一个空签名的 thinking 块——后者直接被服务端拒）
                contents.RemoveAt(i);
                continue;
            }

            // 只剩正文：密文摘掉，正文留着
            // 正文是明文思考（照旧回传，与 ADR 0100「不按产出模型过滤历史思考」一致），
            // 身份留着才能下一轮接着判断——换回原模型时它还在
            contents[i] = new TextReasoningContent(reasoning.Text)
            {
                AdditionalProperties = reasoning.AdditionalProperties,
            };
        }

        return contents == null ? message : ChatMessageCopies.WithContents(message, contents);
    }
}
