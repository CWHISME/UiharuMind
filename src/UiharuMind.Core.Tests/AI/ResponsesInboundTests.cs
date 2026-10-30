using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.Configs.RemoteAI;

#pragma warning disable OPENAI001

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// responses 线带回的加密思考：SDK 把它还原成带 <c>ProtectedData</c> 的思考内容。
///
/// 这是票据闭环的另一半——请求侧已验（原样带出），这里验响应侧（能拿到）。
///
/// <b>关键</b>：载体字段与 chat 线<b>完全相同</b>（<c>TextReasoningContent.ProtectedData</c>），
/// 所以 ADR 0100 定的落盘路径（随消息存进历史）两条线通用，不用为 responses 另存一套。
/// </summary>
public class ResponsesInboundTests
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

    private static async Task<IReadOnlyList<AIContent>> GetContentsAsync(string body)
    {
        var handler = new StubHandler(body);
        var httpHandler = new WireHttpHandler("https://api.example.com/v1/responses", handler);
        var config = new RemoteModelConfig { ModelId = "m", ApiFormat = EApiFormat.Responses };
        IChatClient client = ChatClientFactory.Create(httpHandler, config, "m", "key");

        ChatResponse result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "问题")]);
        return result.Messages.SelectMany(m => m.Contents).ToList();
    }

    /// <summary>响应里的 encrypted_content 还原成 ProtectedData —— 这是「收到票」的那一半</summary>
    [Fact]
    public async Task Inbound_EncryptedContent_BecomesProtectedData()
    {
        IReadOnlyList<AIContent> contents = await GetContentsAsync(ReasoningResponse);

        TextReasoningContent? reasoning = contents.OfType<TextReasoningContent>().FirstOrDefault();
        Assert.NotNull(reasoning);
        Assert.Equal("INBOUND_BLOB", reasoning!.ProtectedData);
    }

    /// <summary>
    /// 载体形态与 chat 线相同：<c>Text</c> 为空、只有 <c>ProtectedData</c>。
    /// 这是 M.E.AI 文档明说合法的形态（模型默认只给密文）。
    /// </summary>
    [Fact]
    public async Task Inbound_UsesTheSameCarrierShapeAsChat()
    {
        IReadOnlyList<AIContent> contents = await GetContentsAsync(ReasoningResponse);

        TextReasoningContent? reasoning = contents.OfType<TextReasoningContent>().FirstOrDefault();
        Assert.NotNull(reasoning);
        Assert.Equal(string.Empty, reasoning!.Text);
        Assert.False(string.IsNullOrEmpty(reasoning.ProtectedData));
    }

    /// <summary>
    /// item id 存进 AdditionalProperties —— 它是这条 item 的标识，供往返回送时对位。
    /// 注意 id <b>每轮都变</b>（实测 rs_&lt;响应id&gt;:rs_&lt;条目id&gt;），
    /// 所以不能靠它定位，得靠 item 本身的顺序（ADR 0100 的不变量）。
    /// </summary>
    [Fact]
    public async Task Inbound_KeepsItemIdForRoundTrip()
    {
        IReadOnlyList<AIContent> contents = await GetContentsAsync(ReasoningResponse);

        TextReasoningContent? reasoning = contents.OfType<TextReasoningContent>().FirstOrDefault();
        Assert.NotNull(reasoning);
        Assert.NotNull(reasoning!.AdditionalProperties);
        Assert.Contains("rs_1:rs_a", JsonSerializer.Serialize(reasoning.AdditionalProperties));
    }

    /// <summary>没有加密思考时不会出现空的思考内容（响应里没有 reasoning item）</summary>
    [Fact]
    public async Task Inbound_WithoutReasoning_ProducesNoReasoningContent()
    {
        const string plain = """
        {"id":"resp_1","object":"response","created_at":1,"model":"m","status":"completed",
         "output":[{"type":"message","id":"msg_1","role":"assistant","status":"completed",
         "content":[{"type":"output_text","text":"答案"}]}]}
        """;

        IReadOnlyList<AIContent> contents = await GetContentsAsync(plain);

        Assert.Empty(contents.OfType<TextReasoningContent>());
    }
}
