using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// Anthropic Messages 线的思考块归一：把「有正文的思考」与「紧邻它的、只有签名的思考」
/// 合成<b>一条</b>。
///
/// <para><b>为什么必须合</b>：Anthropic 的 thinking 是一个块，签名（<c>signature</c>）是块里的字段，
/// 不是独立的块。而它的流式响应把两者拆成两帧发——<c>thinking_delta</c> 送正文，
/// <c>signature_delta</c> 送签名（实测：官方 SDK 把这两帧分别交成两条
/// <see cref="TextReasoningContent"/>，一条有正文无签名、一条有签名无正文）。
/// 官方 SDK <b>不做</b>这个合并：直接把它俩原样回填，请求体里就成了<b>两个</b> thinking 块，
/// 头一个的 <c>signature</c> 是空串——Anthropic 拒收带空签名的 thinking 块，整发请求 400。
///
/// <para><b>合成一条而不是拼成一个块</b>：合并发生在 <see cref="TextReasoningContent"/> 这一层，
/// 由 SDK 自己去转成块。走这一层的好处是密文仍然待在 ADR 0100 定的那个字段里
/// （<see cref="TextReasoningContent.ProtectedData"/>），落盘形态、门禁输入、四条不变量
/// 一行都不用改——换句话说，这一步只是把 SDK 眼中的「两条」还原成协议要的「一块」。
///
/// <para><b>对另外两条线是无副作用的</b>：chat 线的密文是「整块 <c>reasoning_details</c>」，
/// 它的思考正文根本不下发（<see cref="TextReasoningContent.Text"/> 恒为空），
/// 所以「前面那条有正文」的前提压根不成立，这段逻辑整段跳过；
/// responses 线的密文由 SDK 自己往返，也不经过这里。
///
/// <para><b>幂等</b>：合成后的形态是「有正文 + 有密文」，再跑一次不会二次合成。
/// </summary>
internal static class AnthropicThinkingNormalizer
{
    /// <summary>
    /// 归一一批消息里的思考块。
    ///
    /// 没有需要合并的地方时<b>原样返回入参那一份</b>——绝大多数消息（用户消息、
    /// 没有思考的助手回复）一个字节都不该动。
    /// </summary>
    /// <param name="messages">要发给服务端的��史</param>
    /// <returns>归一后的历史；没改动时与入参同一份</returns>
    public static IReadOnlyList<ChatMessage> Normalize(IEnumerable<ChatMessage> messages)
    {
        IReadOnlyList<ChatMessage> source = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();

        List<ChatMessage>? rebuilt = null;
        for (int i = 0; i < source.Count; i++)
        {
            ChatMessage message = source[i];
            List<AIContent>? contents = MergeThinking(message.Contents);
            if (contents == null) continue; //这一条不用改

            // 到这里才知道前面那些消息原样复用即可——不必从头重建
            rebuilt ??= [.. source.Take(i)];
            rebuilt.Add(CloneWith(message, contents));
        }

        return rebuilt ?? source;
    }

    /// <summary>
    /// 合并一条消息里的思考块。需要合并时返回新的内容列表，否则返回 null（表示不用改）。
    /// </summary>
    /// <param name="contents">消息的内容</param>
    /// <returns>合并后的内容；不用改时为 null</returns>
    private static List<AIContent>? MergeThinking(IList<AIContent> contents)
    {
        if (!NeedsMerge(contents)) return null;

        List<AIContent> rebuilt = new(contents.Count);
        int pending = -1; //重建结果里「有正文、无签名」那条思考的位置
        foreach (AIContent content in contents)
        {
            if (content is not TextReasoningContent reasoning)
            {
                pending = -1;
                rebuilt.Add(content);
                continue;
            }

            if (string.IsNullOrEmpty(reasoning.ProtectedData))
            {
                // 有正文没签名：记着位置，等着签名并进来
                if (reasoning.Text.Length > 0) pending = rebuilt.Count;
                rebuilt.Add(reasoning);
                continue;
            }

            if (pending >= 0 && rebuilt[pending] is TextReasoningContent target && target.Text.Length > 0)
            {
                TextReasoningContent merged = new(target.Text) { ProtectedData = reasoning.ProtectedData };
                // 产出方身份跟着走：它是门禁的输入，丢了这一发的票就再也认不出该发给谁
                if (target.AdditionalProperties != null) merged.AdditionalProperties = target.AdditionalProperties;
                else if (reasoning.AdditionalProperties != null) merged.AdditionalProperties = reasoning.AdditionalProperties;
                rebuilt[pending] = merged;
                pending = -1;
                continue; //这条签名不再单独出现
            }

            // 没有可并的正文。正文为空的只剩密文——那是 Anthropic 的 <c>redacted_thinking</c>
            // （脱敏思考）形态，它本来就是一个独立的块，并进别处就发不出去了。
            // 但得<b>还原成它自己</b>：官方 SDK 是靠 <see cref="AIContent.RawRepresentation"/>
            // 认出这一形态的（实测：带上 RedactedThinkingBlock 就发 redacted_thinking，
            // 不带就发成普通 thinking 块、thinking 为空串——那与服务端给的原块对不上）。
            // 而 RawRepresentation 是 provider 的原始对象引用，落盘往返后就没了，
            // 所以这里按「正文为空 + 有密文」这个形态把它重新装回去。
            pending = -1;
            rebuilt.Add(reasoning.Text.Length == 0 ? AsRedactedThinking(reasoning) : reasoning);
        }

        return rebuilt;
    }

    /// <summary>
    /// 把「只有密文没有正文」的思考内容还原成 Anthropic 的脱敏思考块。
    ///
    /// <para><b>为什么能靠形态判</b>：Anthropic 的普通 thinking 块<b>必有正文</b>——
    /// 流式第一帧（<c>content_block_start</c>）给的空 thinking 块<b>不带签名</b>，
    /// 会被空思考判据丢掉。所以「正文为空 + 有密文」在这条线上唯一可能的来源就是脱敏思考。
    /// 判错的代价两边一样（服务端 400），而按形态判至少把真脱敏那一种修好了。</para>
    /// </summary>
    private static TextReasoningContent AsRedactedThinking(TextReasoningContent source)
    {
        TextReasoningContent result = new(string.Empty) { ProtectedData = source.ProtectedData };
        if (source.AdditionalProperties != null) result.AdditionalProperties = source.AdditionalProperties;
        result.RawRepresentation = new RedactedThinkingBlock { Data = source.ProtectedData };
        return result;
    }

    /// <summary>
    /// 这条消息里有没有要动的地方：
    /// <list type="bullet">
    /// <item><description>「前面有正文、后面紧跟一条只有签名」→ 要合成一条；</description></item>
    /// <item><description>「正文为空 + 有密文」→ 要还原成脱敏思考块。</description></item>
    /// </list>
    /// </summary>
    private static bool NeedsMerge(IList<AIContent> contents)
    {
        bool sawText = false;
        foreach (AIContent content in contents)
        {
            if (content is not TextReasoningContent reasoning)
            {
                sawText = false;
                continue;
            }

            if (string.IsNullOrEmpty(reasoning.ProtectedData))
            {
                sawText = reasoning.Text.Length > 0;
                continue;
            }

            // 有密文：前面有正文 → 要合成一条；正文为空 → 要还原成脱敏思考块
            if (sawText) return true;
            if (reasoning.Text.Length == 0) return true;
        }

        return false;
    }

    /// <summary>
    /// 复制一条消息、换掉它的内容。
    ///
    /// <b>不复制 <c>RawRepresentation</c></b>：那是 provider 的原始对象引用，
    /// 落盘后就没了（上游 spec 明说回放不得依赖它）。要带的是能序列化的那些字段。
    /// </summary>
    private static ChatMessage CloneWith(ChatMessage message, List<AIContent> contents)
    {
        return new ChatMessage(message.Role, contents)
        {
            MessageId = message.MessageId,
            AuthorName = message.AuthorName,
            AdditionalProperties = message.AdditionalProperties,
        };
    }
}
