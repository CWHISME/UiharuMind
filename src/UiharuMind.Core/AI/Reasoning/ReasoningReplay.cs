using System.Text;
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

        return (JoinText(message.Contents), ReasoningTicketCarrier.Read(message)?.Json);
    }

    /// <summary>
    /// 拼齐一条消息里的多段思考；没有思考正文时返回 null。
    ///
    /// 老会话的思考是逐 chunk 落下的碎片，要拼齐了发回去——只取其中一段等于把思考截成一句话，
    /// 接口那边照样对不上。只有一段时直接用它的引用，不另拼一份。
    /// </summary>
    /// <param name="contents">消息内容</param>
    /// <returns>拼好的思考正文；没有时为 null</returns>
    private static string? JoinText(IList<AIContent> contents)
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
}
