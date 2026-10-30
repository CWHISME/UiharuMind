using System.Text.Json;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Reasoning;

/// <summary>
/// 一条消息上挂着的加密思考票据。
///
/// <b>票据长在消息上，不在消息之外。</b>
/// 这是 ADR 0100 决定 3 的核心，也是 0100 的「每条助手消息永久挂自己那张票」从
/// 「靠 store 保证」变成「<b>结构上就成立</b>」的地方：
///
/// <list type="bullet">
/// <item><description>票据随消息序列化落盘（<c>history.jsonl</c>），重启后读历史即得，
/// 不需要重建任何映射——0100 的内存版重启就丢，丢掉的那条消息与重启前的字节不一致，
/// 前缀当场分叉。</description></item>
/// <item><description>消息不会被挪走，票也就不会被挪走。0100 靠「按序号存」维持对应关系，
/// 历史一裁剪、一替换，序号就错位；现在对应关系由消息列表本身保证。</description></item>
/// </list>
///
/// 承载位置选 <see cref="TextReasoningContent.ProtectedData"/>：那是 M.E.AI 为这个场景
/// 设计的字段，官方语义是「provider 专属、只回传给 provider、不给人看的密文」。
/// 上游 agent-framework 的 Python 侧用 <c>Content.protected_data</c>、.NET 侧用同一个字段。
///
/// 产出方身份（provider + model）放在同一条思考内容的 <c>AdditionalProperties</c> 上，
/// 与密文同生共死——<b>票据和它的归属必须一起持久化</b>，否则重启后无从判断该发给谁。
/// </summary>
internal static class ReasoningTicketCarrier
{
    /// <summary>产出方供应商的标记键</summary>
    internal const string ProducerProviderKey = "_ticket_provider";

    /// <summary>产出方模型的标记键</summary>
    internal const string ProducerModelKey = "_ticket_model";

    /// <summary>
    /// 把票据挂到一条思考内容上。
    /// </summary>
    /// <param name="reasoning">承载票据的思考内容（原地改）</param>
    /// <param name="ticketJson">票据原文（整个 <c>reasoning_details</c> 数组）</param>
    /// <param name="producer">产出它的供应商与模型</param>
    public static void Attach(TextReasoningContent reasoning, string ticketJson, ReasoningGate producer)
    {
        reasoning.ProtectedData = ticketJson;
        StampProducer(reasoning, producer, overwrite: true);
    }

    /// <summary>
    /// 给一条<b>已经有密文</b>的思考内容补上产出方身份。
    ///
    /// <para>为什么需要「补」而不是一开始就挂上：chat 线的密文是我们自己从字节里切出来的，
    /// 切的时候就知道是谁产出的；responses 与 messages 线的密文是 <b>SDK 解出来的</b>——
    /// 它只还原 <c>ProtectedData</c>（与 item id / 块形状），不知道也不该知道「这台机器上是谁签的票」。
    /// 而身份是门禁的输入：缺了它，落盘后读回来就无从判断该不该发给当前模型，只能一律不发——票白存。</para>
    /// </summary>
    /// <param name="reasoning">承载密文的思考内容</param>
    /// <param name="producer">产出方</param>
    /// <param name="overwrite">
    /// 已有身份时要不要覆盖。默认不覆盖——落盘往返后身份已经在了，
    /// 再补一次会把「原来是谁产的」改成「现在是谁在发的」
    /// </param>
    public static void StampProducer(TextReasoningContent reasoning, ReasoningGate producer, bool overwrite = false)
    {
        if (!overwrite && HasProducer(reasoning)) return;

        reasoning.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        reasoning.AdditionalProperties[ProducerProviderKey] = producer.Provider;
        reasoning.AdditionalProperties[ProducerModelKey] = producer.Model;
    }

    /// <summary>
    /// 身份齐不齐。<b>按键在不在判，不看值</b>：落盘往返后值是 <see cref="JsonElement"/>，
    /// 拿「非空」判会误判成没有
    /// </summary>
    /// <param name="reasoning">承载票据的思考内容</param>
    /// <returns>两个键都在时为 true</returns>
    private static bool HasProducer(TextReasoningContent reasoning) =>
        reasoning.AdditionalProperties is not null
        && reasoning.AdditionalProperties.ContainsKey(ProducerProviderKey)
        && reasoning.AdditionalProperties.ContainsKey(ProducerModelKey);

    /// <summary>
    /// 把流里那条加密思考内容转成<b>持久形态</b>：一条带
    /// <see cref="TextReasoningContent.ProtectedData"/> 的思考内容。
    ///
    /// 不带票据的纯信号内容（只用来开界面那张卡）转成 null——那种内容不进历史。
    /// </summary>
    /// <param name="signal">流里的加密思考内容</param>
    /// <returns>持久形态；纯信号时为 null</returns>
    public static TextReasoningContent? ToPersistent(Execution.EncryptedReasoningContent signal)
    {
        if (string.IsNullOrEmpty(signal.TicketJson)) return null;
        if (string.IsNullOrEmpty(signal.ProducerProvider) || string.IsNullOrEmpty(signal.ProducerModel)) return null;

        TextReasoningContent reasoning = new(string.Empty);
        Attach(reasoning, signal.TicketJson, new ReasoningGate(signal.ProducerProvider, signal.ProducerModel));
        return reasoning;
    }

    /// <summary>
    /// 从一条消息上取回票据与它的产出方。
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>有票据时返回票据与产出方；没有时返回 null</returns>
    public static (string Json, ReasoningGate Producer)? Read(ChatMessage message)
    {
        foreach (AIContent content in message.Contents)
        {
            if (content is not TextReasoningContent { ProtectedData: { Length: > 0 } } reasoning) continue;

            // 产出方身份缺失时不回填：门禁无从判断，宁可不发（ADR 0100 决定 3）
            if (!TryReadProducer(reasoning, out ReasoningGate producer)) return null;
            return (reasoning.ProtectedData, producer);
        }

        return null;
    }

    /// <summary>
    /// 读产出方身份。落盘往返后值是 <see cref="JsonElement"/>，取值一律走
    /// <see cref="object.ToString"/>——不能强转 string。
    /// </summary>
    /// <param name="reasoning">承载票据的思考内容</param>
    /// <param name="producer">读出的产出方；读不出时为 default</param>
    /// <returns>身份完整时为 true</returns>
    internal static bool TryReadProducer(TextReasoningContent reasoning, out ReasoningGate producer)
    {
        producer = default;
        if (reasoning.AdditionalProperties is null) return false;

        if (!reasoning.AdditionalProperties.TryGetValue(ProducerProviderKey, out object? providerRaw)) return false;
        if (!reasoning.AdditionalProperties.TryGetValue(ProducerModelKey, out object? modelRaw)) return false;

        string? provider = providerRaw?.ToString();
        string? model = modelRaw?.ToString();
        if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(model)) return false;

        producer = new ReasoningGate(provider, model);
        return true;
    }
}
