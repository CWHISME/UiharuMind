using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 复制消息的两种形态。**改内容必须走这里**：手写 <c>new ChatMessage(...)</c> 会漏掉
/// <see cref="ChatMessage.MessageId"/> / <see cref="ChatMessage.AuthorName"/> /
/// <see cref="ChatMessage.AdditionalProperties"/>，而那些字段会被框架与落盘逻辑读。
///
/// <para>两处调用方（门禁过滤、Anthropic 思考块归一）原本各写了一份，字段漏一个就是静默丢数据。</para>
/// </summary>
internal static class ChatMessageCopies
{
    /// <summary>
    /// 换掉一条消息的内容。
    ///
    /// <b>不复制 <see cref="AIContent.RawRepresentation"/> 所在的整条消息级原始对象</b>：
    /// 那是 provider 的原始引用，落盘后就没了，回放不得依赖它。要带的是能序列化的那些字段。
    /// </summary>
    /// <param name="message">原消息</param>
    /// <param name="contents">新的内容列表</param>
    /// <returns>新消息</returns>
    public static ChatMessage WithContents(ChatMessage message, List<AIContent> contents) =>
        new(message.Role, contents)
        {
            MessageId = message.MessageId,
            AuthorName = message.AuthorName,
            AdditionalProperties = message.AdditionalProperties,
        };
}
