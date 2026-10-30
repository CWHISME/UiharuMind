using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.AI.Reasoning;
using UiharuMind.Core.Configs.RemoteAI;

#pragma warning disable OPENAI001

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// responses 线的票据闭环：SDK 解出的密文要能被门禁认出是「谁产的」。
///
/// 这一步补的是一个真实缺口：chat 线的密文是我们自己切的，切的时候就知道产出方；
/// responses 线的密文是 SDK 解的，它只还原 <c>ProtectedData</c>，不知道产出方。
/// 身份缺了，落盘的票读回来就无从判断该不该发给当前模型——只能一律不发，票白存。
/// </summary>
public class ResponsesTicketProducerTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private const string ReasoningResponse = """
    {"id":"resp_1","object":"response","created_at":1,"model":"m","status":"completed",
     "output":[{"type":"reasoning","id":"rs_1:rs_a","summary":[],"encrypted_content":"INBOUND_BLOB","status":"completed"},
                {"type":"message","id":"msg_1","role":"assistant","status":"completed",
                 "content":[{"type":"output_text","text":"答案"}]}]}
    """;

    private static async Task<ChatMessage> GetAssistantMessageAsync()
    {
        var handler = new StubHandler(ReasoningResponse);
        var httpHandler = new WireHttpHandler("https://api.example.com/v1/responses", handler);
        var config = new RemoteModelConfig { ModelId = "m", ApiFormat = EApiFormat.Responses };
        IChatClient client = ChatClientFactory.Create(httpHandler, config, "m", "key");

        ChatResponse result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "问题")]);
        return result.Messages.First(m => m.Role == ChatRole.Assistant);
    }

    /// <summary>
    /// SDK 解出的思考内容本身没有产出方身份——这是补身份那一步存在的理由。
    /// </summary>
    [Fact]
    public async Task SdkProduced_Content_HasNoProducerIdentity()
    {
        ChatMessage message = await GetAssistantMessageAsync();

        TextReasoningContent? reasoning = message.Contents.OfType<TextReasoningContent>().FirstOrDefault();
        Assert.NotNull(reasoning);
        Assert.Equal("INBOUND_BLOB", reasoning!.ProtectedData);
        // SDK 只还原密文与 item id，不认「谁签的票」
        Assert.False(reasoning.AdditionalProperties?.ContainsKey(ReasoningTicketCarrier.ProducerProviderKey) ?? false);
    }

    /// <summary>
    /// 补上身份之后，这条内容就能被 <see cref="ReasoningTicketCarrier.Read"/> 认出——
    /// 它是门禁读票据的唯一入口，认不出就等于票不存在。
    /// </summary>
    [Fact]
    public async Task AfterStamping_CarrierCanReadTheTicket()
    {
        ChatMessage message = await GetAssistantMessageAsync();
        TextReasoningContent? reasoning = message.Contents.OfType<TextReasoningContent>().FirstOrDefault();
        Assert.NotNull(reasoning);

        LazyChatClient.StampTicketProducer(reasoning!, new ReasoningGate("provider-a", "model-a"));

        (string Json, ReasoningGate Producer)? ticket = ReasoningTicketCarrier.Read(message);

        Assert.NotNull(ticket);
        Assert.Equal("INBOUND_BLOB", ticket!.Value.Json);
        Assert.Equal("provider-a", ticket.Value.Producer.Provider);
        Assert.Equal("model-a", ticket.Value.Producer.Model);
    }

    /// <summary>
    /// 补身份不能覆盖 SDK 放的 item id——那个字段是 responses 线往返回送时对位用的，
    /// 覆盖了会让 item 找不到位置。
    /// </summary>
    [Fact]
    public async Task Stamping_KeepsTheSdkItemId()
    {
        ChatMessage message = await GetAssistantMessageAsync();
        TextReasoningContent? reasoning = message.Contents.OfType<TextReasoningContent>().FirstOrDefault();
        Assert.NotNull(reasoning);

        LazyChatClient.StampTicketProducer(reasoning!, new ReasoningGate("provider-a", "model-a"));

        Assert.Contains("reasoningItemId", JsonSerializer.Serialize(reasoning!.AdditionalProperties));
    }

    /// <summary>
    /// 补身份不能覆盖已有的身份：落盘往返后身份已经在了，
    /// 再补一次会把「原来是谁产的」改成「现在是谁在发」——那是张冠李戴。
    /// </summary>
    [Fact]
    public async Task Stamping_DoesNotOverwriteExistingIdentity()
    {
        ChatMessage message = await GetAssistantMessageAsync();
        TextReasoningContent? reasoning = message.Contents.OfType<TextReasoningContent>().FirstOrDefault();
        Assert.NotNull(reasoning);

        LazyChatClient.StampTicketProducer(reasoning!, new ReasoningGate("provider-a", "model-a"));
        // 第二次补一个不同的产出方
        LazyChatClient.StampTicketProducer(reasoning!, new ReasoningGate("provider-b", "model-b"));

        (string Json, ReasoningGate Producer)? ticket = ReasoningTicketCarrier.Read(message);
        Assert.NotNull(ticket);
        Assert.Equal("provider-a", ticket!.Value.Producer.Provider);
    }
}
