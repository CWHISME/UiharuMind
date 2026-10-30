using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Reasoning;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 票据的门禁过滤：换模型后旧模型的票这一轮不带（ADR 0100 不变量 3）。
///
/// <para>为什么这道门禁要单独一层：早先它长在 chat 线专用的「按序号收集」里，
/// responses 线与 messages 线的密文是 <b>SDK 自己从消息里读</b>的（<c>ProtectedData</c>
/// → <c>encrypted_content</c> / <c>thinking.signature</c>），不经过那道收集——
/// 于是换模型后旧票会被一起发出去，而网关<b>不校验归属</b>（实测照样 200），
/// 发错了不会报错，只会安静地错。</para>
///
/// <para>钉住四条语义：<b>不过滤时不分配</b>、<b>历史里的票不动</b>（换回原模型还能接上）、
/// <b>身份读不出来也不带</b>、<b>只剩空思考时整条丢掉</b>（各协议都不收空思考块）。</para>
/// </summary>
public class ReasoningGateFilterTests
{
    private static readonly ReasoningGate Current = new("prov-a", "model-a");

    /// <summary>造一条带票据的思考内容，产出方可指定</summary>
    private static TextReasoningContent Ticket(string provider, string model, string text = "",
        string cipher = "BLOB")
    {
        TextReasoningContent reasoning = new(text) { ProtectedData = cipher };
        reasoning.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        reasoning.AdditionalProperties[ReasoningTicketCarrier.ProducerProviderKey] = provider;
        reasoning.AdditionalProperties[ReasoningTicketCarrier.ProducerModelKey] = model;
        return reasoning;
    }

    private static ChatMessage Assistant(params AIContent[] contents) => new(ChatRole.Assistant, contents);

    [Fact]
    public void SameProducer_TicketIsKept()
    {
        List<ChatMessage> messages = [Assistant(Ticket("prov-a", "model-a"))];

        IReadOnlyList<ChatMessage> result = ReasoningGateFilter.Apply(messages, Current);

        Assert.Same(messages, result); //零分配
        Assert.Equal("BLOB", result[0].Contents.OfType<TextReasoningContent>().Single().ProtectedData);
    }

    /// <summary>
    /// 换模型：票这一轮不带。但<b>历史里的原票不动</b>——换回原模型还能接上
    /// （ADR 0100：门禁不过时不能删票，只是这一轮不带）。
    /// </summary>
    [Fact]
    public void DifferentProducer_TicketIsStrippedFromThisRound()
    {
        ChatMessage original = Assistant(Ticket("prov-b", "model-b"));
        List<ChatMessage> messages = [original];

        IReadOnlyList<ChatMessage> result = ReasoningGateFilter.Apply(messages, Current);

        Assert.NotSame(messages, result);
        Assert.Empty(result[0].Contents.OfType<TextReasoningContent>());

        // 历史里那张票还在
        Assert.Equal("BLOB", original.Contents.OfType<TextReasoningContent>().Single().ProtectedData);
    }

    /// <summary>
    /// 只比 model id 不够：同一个 model id 在不同供应商下是不同的东西，
    /// 票是原厂签的，跨供应商同样不认。
    /// </summary>
    [Fact]
    public void SameModelDifferentProvider_TicketIsStripped()
    {
        List<ChatMessage> messages = [Assistant(Ticket("prov-b", "model-a"))];

        IReadOnlyList<ChatMessage> result = ReasoningGateFilter.Apply(messages, Current);

        Assert.Empty(result[0].Contents.OfType<TextReasoningContent>());
    }

    /// <summary>
    /// 身份读不出来时也不带：门禁无从判断，宁可不发——
    /// 与 <see cref="ReasoningTicketCarrier.Read"/> 同一个口径。
    /// </summary>
    [Fact]
    public void MissingProducerIdentity_TicketIsStripped()
    {
        List<ChatMessage> messages =
        [
            Assistant(new TextReasoningContent(string.Empty) { ProtectedData = "BLOB" }),
        ];

        IReadOnlyList<ChatMessage> result = ReasoningGateFilter.Apply(messages, Current);

        Assert.Empty(result[0].Contents.OfType<TextReasoningContent>());
    }

    /// <summary>
    /// 有正文的思考：密文摘掉，<b>正文留着</b>。
    /// 正文是明文思考，照旧回传（ADR 0100：不按产出模型过滤历史思考）。
    /// </summary>
    [Fact]
    public void BlockedTicket_KeepsThePlainText()
    {
        List<ChatMessage> messages = [Assistant(Ticket("prov-b", "model-b", text: "思考正文"))];

        IReadOnlyList<ChatMessage> result = ReasoningGateFilter.Apply(messages, Current);

        TextReasoningContent kept = result[0].Contents.OfType<TextReasoningContent>().Single();
        Assert.Equal("思考正文", kept.Text);
        Assert.True(string.IsNullOrEmpty(kept.ProtectedData));
    }

    /// <summary>
    /// 明文思考正文不受门禁管：门禁管的是密文，正文照旧回传。
    /// </summary>
    [Fact]
    public void PlainReasoningText_IsNotAffected()
    {
        List<ChatMessage> messages = [Assistant(new TextReasoningContent("思考正文"))];

        IReadOnlyList<ChatMessage> result = ReasoningGateFilter.Apply(messages, Current);

        Assert.Same(messages, result);
        Assert.Equal("思考正文", result[0].Contents.OfType<TextReasoningContent>().Single().Text);
    }

    /// <summary>
    /// 消息结构不变：角色、内容条数、消息 id 都原样——过滤只动密文那一项。
    /// </summary>
    [Fact]
    public void Filter_KeepsMessageShape()
    {
        ChatMessage original = new(ChatRole.Assistant,
        [
            Ticket("prov-b", "model-b", text: "思考正文"),
            new TextContent("答案"),
        ])
        {
            MessageId = "msg-1",
            AuthorName = "作者",
        };
        List<ChatMessage> messages = [original];

        ChatMessage filtered = ReasoningGateFilter.Apply(messages, Current)[0];

        Assert.Equal(ChatRole.Assistant, filtered.Role);
        Assert.Equal(2, filtered.Contents.Count);
        Assert.Equal("msg-1", filtered.MessageId);
        Assert.Equal("作者", filtered.AuthorName);
    }

    /// <summary>
    /// 产出方身份要留着：下一轮还得靠它判断，换回原模型时它还在。
    /// </summary>
    [Fact]
    public void Filter_KeepsProducerIdentity()
    {
        List<ChatMessage> messages = [Assistant(Ticket("prov-b", "model-b", text: "思考正文"))];

        TextReasoningContent filtered = ReasoningGateFilter.Apply(messages, Current)[0]
            .Contents.OfType<TextReasoningContent>().Single();

        Assert.NotNull(filtered.AdditionalProperties);
        Assert.Equal("prov-b", filtered.AdditionalProperties![ReasoningTicketCarrier.ProducerProviderKey]!.ToString());
    }

    /// <summary>
    /// 门禁通过的消息在混排里也不该被克隆——只有真要摘的那几条才重建。
    /// </summary>
    [Fact]
    public void OnlyBlockedMessagesAreCloned()
    {
        ChatMessage passing = Assistant(Ticket("prov-a", "model-a"));
        ChatMessage blocked = Assistant(Ticket("prov-b", "model-b"));
        List<ChatMessage> messages = [passing, blocked];

        IReadOnlyList<ChatMessage> result = ReasoningGateFilter.Apply(messages, Current);

        Assert.Same(passing, result[0]);
        Assert.NotSame(blocked, result[1]);
    }
}
