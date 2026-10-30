using Anthropic.Models.Messages;
using UiharuMind.Core.AI.Chat;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// Anthropic Messages 线的思考块归一：把 SDK 交出来的思考内容，还原成 Anthropic 认的块形状。
///
/// <para><b>Anthropic 的 thinking 是一个块</b>，签名（<c>signature</c>）是块里的字段，
/// 不是独立的块。而它的流式响应把两者拆成两帧发——<c>thinking_delta</c> 送正文，
/// <c>signature_delta</c> 送签名（实测：官方 SDK 把这两帧分别交成两条
/// <see cref="TextReasoningContent"/>，一条有正文无签名、一条有签名无正文）。
/// 官方 SDK <b>不做</b>这个合并：原样回填，请求体里就成了<b>两个</b> thinking 块，
/// 头一个的 <c>signature</c> 是空串——Anthropic 拒收带空签名的 thinking 块，整发请求 400。</para>
///
/// <para><b>归到 <see cref="TextReasoningContent"/> 这一层做</b>，由 SDK 自己去转成块。
/// 走这一层的好处是密文仍然待在 ADR 0100 定的那个字段里
/// （<see cref="TextReasoningContent.ProtectedData"/>），落盘形态、门禁输入、四条不变量
/// 一行都不用改——这一步只是把 SDK 眼中的「两条」还原成协议要的「一块」。</para>
///
/// <para><b>三条形态规则</b>（对应 Anthropic 的三种块）：</para>
/// <list type="number">
/// <item><description><b>有正文 + 有签名</b>：正常，原样保留；</description></item>
/// <item><description><b>有正文、无签名</b>：Anthropic 拒收无签名的 thinking 块
/// （签名就是它认凭证的依据），所以要么并进后面那条的签名、要么整个丢掉；</description></item>
/// <item><description><b>无正文 + 有签名</b>：脱敏思考，还原成 <c>redacted_thinking</c> 块。</description></item>
/// </list>
///
/// <para><b>对另外两条线是无副作用的</b>：chat 线的密文是「整块 <c>reasoning_details</c>」，
/// 它的思考正文根本不下发（<see cref="TextReasoningContent.Text"/> 恒为空），
/// 所以「前面那条有正文」的前提压根不成立，这段逻辑整段跳过；
/// responses 线的密文由 SDK 自己往返，也不经过这里。</para>
///
/// <para><b>幂等</b>：归一后的形态是「有正文 + 有签名」，再跑一次不会二次合成。</para>
/// </summary>
internal static class AnthropicThinkingNormalizer
{
    /// <summary>
    /// 归一一批消息里的思考块。
    ///
    /// 没有需要动的地方时<b>原样返回入参那一份</b>——绝大多数消息（用户消息、
    /// 没有思考的助手回复）一个字节都不该动。
    /// </summary>
    /// <param name="messages">要发给服务端的历史</param>
    /// <returns>归一后的历史；没改动时与入参同一份</returns>
    public static IReadOnlyList<ChatMessage> Normalize(IEnumerable<ChatMessage> messages)
    {
        IReadOnlyList<ChatMessage> source = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();

        List<ChatMessage>? rebuilt = null;
        for (int i = 0; i < source.Count; i++)
        {
            ChatMessage message = source[i];
            List<AIContent>? contents = Rewrite(message.Contents);
            if (contents == null) continue; //这一条不用改

            // 到这里才知道前面那些消息原样复用即可——不必从头重建
            rebuilt ??= [.. source.Take(i)];
            rebuilt.Add(ChatMessageCopies.WithContents(message, contents));
        }

        return rebuilt ?? source;
    }

    /// <summary>
    /// 归正一条消息里的思考块。需要动时返回新的内容列表，否则返回 null（表示不用改）。
    /// </summary>
    /// <param name="contents">消息的内容</param>
    /// <returns>归正后的内容；不用改时为 null</returns>
    private static List<AIContent>? Rewrite(IList<AIContent> contents)
    {
        if (!NeedsRewrite(contents)) return null;

        List<AIContent> rebuilt = new(contents.Count);
        // 「有正文、无签名」那条：等后面紧跟的签名并进来。还没进 rebuilt——
        // 万一等到的不是签名（而是另一个块），它就得被丢掉（Anthropic 不收无签名的 thinking 块）
        TextReasoningContent? pendingText = null;

        foreach (AIContent content in contents)
        {
            if (content is not TextReasoningContent reasoning)
            {
                pendingText = null;
                rebuilt.Add(content);
                continue;
            }

            if (string.IsNullOrEmpty(reasoning.ProtectedData))
            {
                if (reasoning.Text.Length == 0)
                {
                    pendingText = null; //空思考：丢
                    continue;
                }

                pendingText = reasoning;
                continue;
            }

            // 有密文。正文为空的那条是「只带签名来的」——并进待并的正文
            if (pendingText != null && reasoning.Text.Length == 0)
            {
                TextReasoningContent merged = new(pendingText.Text) { ProtectedData = reasoning.ProtectedData };
                // 产出方身份跟着走：它是门禁的输入，丢了这一发的票就再也认不出该发给谁
                if (pendingText.AdditionalProperties != null) merged.AdditionalProperties = pendingText.AdditionalProperties;
                else if (reasoning.AdditionalProperties != null) merged.AdditionalProperties = reasoning.AdditionalProperties;
                rebuilt.Add(merged);
                pendingText = null;
                continue;
            }

            // 等不到签名：那条无签名的正文作废（Anthropic 不收），当前这条按自己的形态处理
            pendingText = null;
            if (reasoning.Text.Length == 0) rebuilt.Add(AsRedactedThinking(reasoning));
            else rebuilt.Add(reasoning);
        }

        // 遍历结束还挂着一个没等到签名的正文：作废，不发给 Anthropic
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
        // 调用方保证密文非空（只有「有密文」那条才会走到这里）
        string data = source.ProtectedData ?? string.Empty;
        TextReasoningContent result = new(string.Empty) { ProtectedData = data };
        if (source.AdditionalProperties != null) result.AdditionalProperties = source.AdditionalProperties;
        result.RawRepresentation = new RedactedThinkingBlock { Data = data };
        return result;
    }

    /// <summary>
    /// 这条消息里有没有要动的地方：
    /// <list type="bullet">
    /// <item><description>「有正文、无签名」→ 要么并、要么丢；</description></item>
    /// <item><description>「无正文、有签名」→ 要还原成脱敏思考块。</description></item>
    /// </list>
    /// </summary>
    private static bool NeedsRewrite(IList<AIContent> contents)
    {
        foreach (AIContent content in contents)
        {
            if (content is not TextReasoningContent reasoning) continue;

            if (string.IsNullOrEmpty(reasoning.ProtectedData))
            {
                if (reasoning.Text.Length > 0) return true; //无签名的正文：要么并、要么丢
                continue;
            }

            if (reasoning.Text.Length == 0) return true; //脱敏思考：要还原成它自己
        }

        return false;
    }

}
