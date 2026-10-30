using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Reasoning;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 加密思考票据的四条不变量（ADR 0100）。
///
/// 这四条是上一版实现翻车的地方，每一条都对应一次真实失败：
///   1. 每条助手消息永久挂自己那张票　　← 上一版只挂最后一条，票逐轮漂移
///   2. 逐轮原样重发　　　　　　　　　　← 上一版用一次即消费，历史字节每轮都变
///   3. 换模型时整条丢弃　　　　　　　　← 上一版只比 model id，网关根本不校验归属
///   4. 跨重启不丢　　　　　　　　　　　← 上一版只在内存，重启即全冷
///
/// ADR 0100 决定 3 之后票据<b>长在消息上</b>（随消息落盘），所以第 4 条不再只是
/// 「会话存活期内不丢」——这里直接测「序列化再读回来，票还在」。
/// </summary>
public class ReasoningTicketInvariantTests
{
    private const string TicketA = """[{"type":"reasoning.encrypted","data":"AAA","id":"rs_1:rs_a","format":"probe-v1","index":0}]""";
    /// <summary>票据的产出方：某个闭源模型</summary>
    private static readonly ReasoningGate Producer = new("provider-a", "model-a");

    private const string TicketB = """[{"type":"reasoning.encrypted","data":"BBB","id":"rs_2:rs_b","format":"probe-v1","index":0}]""";

    /// <summary>造一条挂着票据的助手消息</summary>
    private static ChatMessage AssistantWithTicket(string ticketJson, ReasoningGate producer, string text = "回复")
    {
        TextReasoningContent reasoning = new(string.Empty);
        ReasoningTicketCarrier.Attach(reasoning, ticketJson, producer);
        return new ChatMessage(ChatRole.Assistant, [reasoning, new TextContent(text)]);
    }

    /// <summary>
    /// 不变量 1：产出时挂在第 k 条助手消息上，以后每轮都挂在那条——票不会被挪走。
    ///
    /// 上一版的形状是「只挂最后一条」，于是每轮票都往新位置跑，历史里上一轮挂过票的那条
    /// 变成无票形态，字节变了 → 前缀分叉。这里钉的是：位置一旦定下就不再变。
    /// </summary>
    [Fact]
    public void Ticket_StaysOnTheMessageThatProducedIt()
    {
        ChatMessage first = AssistantWithTicket(TicketA, Producer, "a1");
        ChatMessage second = AssistantWithTicket(TicketB, Producer, "a2");

        //第 0 条还是挂 A，第 1 条挂 B——两张票各归各位
        Assert.Equal(TicketA, ReasoningTicketCarrier.Read(first)!.Value.Json);
        Assert.Equal(TicketB, ReasoningTicketCarrier.Read(second)!.Value.Json);
    }

    /// <summary>
    /// 不变量 2：一条消息的票据字节从产生那刻起不再变。
    ///
    /// 这是缓存前缀稳定的前提。上一版「用一次即消费」让第 1 轮挂过的位置在第 2 轮变成无票，
    /// 那一条的字节变了 → 从那条起缓存全冷。
    /// </summary>
    [Fact]
    public void Ticket_BytesNeverChangeAcrossRounds()
    {
        ChatMessage message = AssistantWithTicket(TicketA, Producer);

        string first = ReasoningTicketCarrier.Read(message)!.Value.Json;
        string second = ReasoningTicketCarrier.Read(message)!.Value.Json;
        string third = ReasoningTicketCarrier.Read(message)!.Value.Json;

        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    /// <summary>
    /// 不变量 3：换模型时整条丢弃——provider 与 model 都要对上。
    ///
    /// 实测网关**不校验**模型归属：把某模型的票发给另一个模型照样 200。
    /// 所以这道门禁是唯一的，漏了不会报错，只会安静地错。
    /// </summary>
    [Fact]
    public void Gate_RejectsWhenProviderOrModelDiffers()
    {
        Assert.True(Producer.Allows("provider-a", "model-a")); //完全一致
        Assert.False(Producer.Allows("provider-b", "model-a")); //换供应商
        Assert.False(Producer.Allows("provider-a", "model-b")); //换模型
    }

    /// <summary>
    /// 不变量 4：跨重启不丢——票据随消息落盘，读回来还在。
    ///
    /// 这是 ADR 0100 决定 3 的核心：上一版票据只在内存，进程退出即丢，重启后历史里挂过票的
    /// 消息变回无票形态，那一条起前缀全冷（实测丢掉七成缓存）。
    /// </summary>
    [Fact]
    public void Ticket_SurvivesSerializationRoundTrip()
    {
        ChatMessage message = AssistantWithTicket(TicketA, Producer, "a1");

        string json = JsonSerializer.Serialize(message, SessionJsonOptions.Default.GetTypeInfo(typeof(ChatMessage)));
        ChatMessage? restored = JsonSerializer.Deserialize(json, SessionJsonOptions.Default.GetTypeInfo(typeof(ChatMessage))) as ChatMessage;

        Assert.NotNull(restored);
        (string Json, ReasoningGate Producer)? ticket = ReasoningTicketCarrier.Read(restored!);
        Assert.NotNull(ticket);
        Assert.Equal(TicketA, ticket!.Value.Json);          //密文逐字节相同
        Assert.Equal("provider-a", ticket.Value.Producer.Provider); //归属也还在
        Assert.Equal("model-a", ticket.Value.Producer.Model);
    }

    /// <summary>
    /// 规整历史不能把带密文的思考当空思考丢掉——丢了票，下一发就回填不了。
    /// </summary>
    [Fact]
    public void Normalizer_KeepsTheTicket()
    {
        ChatMessage message = AssistantWithTicket(TicketA, Producer, "a1");

        ChatContentNormalizer.Normalize(message);

        Assert.Contains(message.Contents, x => x is TextReasoningContent { ProtectedData: not null });
        Assert.Equal(TicketA, ReasoningTicketCarrier.Read(message)!.Value.Json);
    }

    /// <summary>换模型时不是「删掉票」，而是这一轮不带——历史里的票留着，换回来还能用</summary>
    [Fact]
    public void Gate_SkipsTheTicket_WithoutDeletingIt()
    {
        ChatMessage message = AssistantWithTicket(TicketA, Producer);
        ReasoningGate other = new("provider-b", "model-b");

        Assert.False(Producer.Allows(other.Provider, other.Model)); //门禁不过，这一轮不带
        Assert.Equal(TicketA, ReasoningTicketCarrier.Read(message)!.Value.Json); //但票本身还在
    }

    /// <summary>
    /// 拼回请求体时，票据挂在<b>原来那条</b>助手消息上。
    ///
    /// 上一次的实现把票挂最后一条，于是每轮位置都变——这条钉住「按序号挂」。
    /// </summary>
    [Fact]
    public void SplicedTicket_LandsOnItsOwnMessage()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """
            {"messages":[
              {"role":"user","content":"u1"},
              {"role":"assistant","content":"a1"},
              {"role":"user","content":"u2"},
              {"role":"assistant","content":"a2"}
            ]}
            """,
            new(null, false, null, false, ReasoningTickets: [TicketA, null]));

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;

        Assert.Equal(TicketA, messages[1]!["reasoning_details"]!.ToJsonString());
        Assert.Null(messages[3]!["reasoning_details"]); //第二条没票就不该有字段
    }

    /// <summary>缺的位置不补：一条消息都没有票时，请求体里不该冒出空数组</summary>
    [Fact]
    public void MissingSlotsStayAbsent()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"a"}]}""",
            new(null, false, null, false, ReasoningTickets: [null]));

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;

        Assert.Null(messages[0]!["reasoning_details"]);
    }

    /// <summary>
    /// 明文摘要不进回填——服务端不认摘要回传，带回去是白改请求体。
    /// 摘要走另一条路：摘给界面显示。
    /// </summary>
    [Fact]
    public void Summary_IsReadableButNotReplayed()
    {
        const string frame =
            """{"choices":[{"delta":{"reasoning_details":[{"type":"reasoning.summary","summary":"先看暂存区再决定","id":"rs_1","format":"anthropic-claude-v1","index":0}]}}]}""";

        Assert.True(ReasoningDetailExtractor.TryExtract(Encoding.UTF8.GetBytes(frame), out byte[] value));
        Assert.Equal("先看暂存区再决定", ReasoningDetailExtractor.ReadSummary(value));
    }

    /// <summary>加密条目没有明文摘要：摘出来是空的，不能编</summary>
    [Fact]
    public void EncryptedDetail_HasNoSummary()
    {
        const string frame =
            """{"choices":[{"delta":{"reasoning_details":[{"type":"reasoning.encrypted","data":"AAA"}]}}]}""";

        Assert.True(ReasoningDetailExtractor.TryExtract(Encoding.UTF8.GetBytes(frame), out byte[] value));
        Assert.Null(ReasoningDetailExtractor.ReadSummary(value));
    }

    /// <summary>
    /// 流里那条加密思考内容落盘时要转成持久的思考内容，而不是被摘掉。
    /// </summary>
    [Fact]
    public void SignalContent_BecomesPersistentTicket()
    {
        EncryptedReasoningContent signal = EncryptedReasoningContent.WithTicket(
            TicketA, "provider-a", "model-a");

        TextReasoningContent? persistent = ReasoningTicketCarrier.ToPersistent(signal);

        Assert.NotNull(persistent);
        Assert.Equal(TicketA, persistent!.ProtectedData);
    }

    /// <summary>纯信号内容（只用来开界面那张卡）不进历史</summary>
    [Fact]
    public void PlainSignalContent_BecomesNothing()
    {
        Assert.Null(ReasoningTicketCarrier.ToPersistent(EncryptedReasoningContent.Instance));
    }
}
