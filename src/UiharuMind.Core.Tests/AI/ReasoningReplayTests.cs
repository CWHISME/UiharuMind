using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Reasoning;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 从一条消息身上取它的思考：明文正文（碎片要拼齐）与加密票据。
///
/// 取法只有这一处（ADR 0101）——收的人（绑定器）与判的人（门禁、模型开关）看的是同一份。
/// </summary>
public class ReasoningReplayTests
{
    private static readonly ReasoningGate Producer = new("provider-a", "model-a");

    private static ChatMessage WithTicket(string ticket, string text = "")
    {
        TextReasoningContent reasoning = new(text) { ProtectedData = ticket };
        ReasoningTicketCarrier.StampProducer(reasoning, Producer);
        return new ChatMessage(ChatRole.Assistant, [reasoning]);
    }

    [Fact]
    public void Read_ReturnsBothTextAndTicket()
    {
        (string? text, string? ticket) = ReasoningReplay.Read(
            WithTicket("""[{"type":"reasoning.encrypted","data":"A"}]""", "想一"));

        Assert.Equal("想一", text);
        Assert.Equal("""[{"type":"reasoning.encrypted","data":"A"}]""", ticket);
    }

    /// <summary>
    /// 正文取第一段非空的。碎片在落盘往返前就由 <c>ChatContentNormalizer</c> 并回一段，
    /// 落盘之后读回来也只有一段；多段只可能是「正文一段 + 只有密文的那段」。
    /// </summary>
    [Fact]
    public void Read_TakesTheFirstReasoningText()
    {
        TextReasoningContent ticketOnly = new(string.Empty)
        {
            ProtectedData = """[{"type":"reasoning.encrypted","data":"A"}]""",
        };
        ReasoningTicketCarrier.StampProducer(ticketOnly, Producer);
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("正文"), ticketOnly]);

        (string? text, string? ticket) = ReasoningReplay.Read(message);

        Assert.Equal("正文", text);
        Assert.NotNull(ticket); // 票据来自另一段，不受影响
    }

    /// <summary>只有票没有正文（闭源模型）也要能取出来——那正是票据机制存在的场景</summary>
    [Fact]
    public void Read_TicketWithoutText()
    {
        (string? text, string? ticket) = ReasoningReplay.Read(WithTicket("""[{"type":"reasoning.encrypted","data":"A"}]"""));

        Assert.Null(text);
        Assert.NotNull(ticket);
    }

    /// <summary>没有思考的两样都是 null——绑定器据此一次都不动手</summary>
    [Fact]
    public void Read_NothingToReplay()
    {
        (string? text, string? ticket) = ReasoningReplay.Read(new ChatMessage(ChatRole.Assistant, "答"));

        Assert.Null(text);
        Assert.Null(ticket);
    }

    /// <summary>非助手消息一律不取：用户消息上即使挂着思考内容也不是要回传的东西</summary>
    [Fact]
    public void Read_IgnoresNonAssistantMessages()
    {
        ChatMessage message = new(ChatRole.User, [new TextReasoningContent("想")]);

        (string? text, string? ticket) = ReasoningReplay.Read(message);

        Assert.Null(text);
        Assert.Null(ticket);
    }
}
