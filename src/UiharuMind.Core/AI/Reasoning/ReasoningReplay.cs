using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Reasoning;

/// <summary>
/// 一条消息身上「要跟着它一起发回去的思考」：明文正文与加密票据。
///
/// <para><b>这个类型存在的理由</b>：回填不再按「第几条助手消息」对齐（ADR 0101），
/// 而是<b>就着这一条消息</b>取它自己的两样东西。取法只此一处——
/// 收的人（绑定器）与判的人（门禁、模型开关）看的是同一份。</para>
/// </summary>
internal static class ReasoningReplay
{
    /// <summary>
    /// 取一条消息上的思考正文与票据。
    ///
    /// <para>门禁已在 <see cref="ReasoningGateFilter"/> 过了一遍，这里只管取：
    /// 再比一次是死条件，只会让「门禁在哪」多一个说法。票据读不出产出方时
    /// <see cref="ReasoningTicketCarrier.Read"/> 会给 null——那是刻意的（ADR 0100 决定 3）。</para>
    /// </summary>
    /// <param name="message">历史消息</param>
    /// <returns>明文正文（没有时为 null）与票据原文（没有时为 null）</returns>
    public static (string? Text, string? Ticket) Read(ChatMessage message)
    {
        if (message.Role != ChatRole.Assistant) return (null, null);

        return (TextOf(message.Contents), ReasoningTicketCarrier.Read(message)?.Json);
    }

    /// <summary>
    /// 一条消息的思考正文。取第一段非空的：落盘往返前碎片已由
    /// <see cref="Chat.ChatContentNormalizer"/> 并回一段，落盘之后读回来也只有一段；
    /// 再往后多段只可能是「正文一段 + 只有密文的那段」，取前者就是全部正文。
    /// </summary>
    /// <param name="contents">消息内容</param>
    /// <returns>思考正文；没有时为 null</returns>
    private static string? TextOf(IList<AIContent> contents)
    {
        foreach (AIContent content in contents)
        {
            if (content is TextReasoningContent { Text.Length: > 0 } reasoning) return reasoning.Text;
        }

        return null;
    }
}
