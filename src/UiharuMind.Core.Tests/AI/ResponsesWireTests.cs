using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.AI.Reasoning;
using UiharuMind.Core.Configs.RemoteAI;

#pragma warning disable OPENAI001

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// responses 线的请求体形状。
///
/// 这条线与 chat 线的差别全在请求体上，钉住三件事：
/// 1. <c>input</c> 是 item 数组（不是 chat 的 messages）
/// 2. <c>include</c> 带 <c>reasoning.encrypted_content</c> —— 不显式要就没有密文下发
/// 3. <c>store</c> 为 false —— 服务端不存我们的响应，密文由客户端保管
///
/// 另外钉住 ADR 0100 的核心：带密文的思考内容<b>原样</b>往返（字节不变）。
/// </summary>
public class ResponsesWireTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content != null)
                LastBody = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"id":"resp_1","object":"response","created_at":1,"model":"m","status":"completed",
                     "output":[{"type":"message","id":"msg_1","role":"assistant","status":"completed",
                     "content":[{"type":"output_text","text":"答案"}]}]}
                    """,
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    private static async Task<JsonNode?> SendBodyAsync(List<ChatMessage> messages)
    {
        var handler = new StubHandler();
        var httpHandler = new OpenAICompatibleHttpHandler("https://api.example.com/v1/responses", handler);
        var config = new RemoteModelConfig { ModelId = "m", ApiFormat = EApiFormat.Responses };

        IChatClient client = OpenAICompatibleChatClient.Create(httpHandler, config, "m", "key");
        try
        {
            await client.GetResponseAsync(messages);
        }
        catch
        {
            // 响应形状不被接受没关系——要看的是请求体
        }

        return handler.LastBody is null ? null : JsonNode.Parse(handler.LastBody);
    }

    private static List<ChatMessage> HistoryWithTicket() =>
    [
        new(ChatRole.User, "问题"),
        new(ChatRole.Assistant,
        [
            new TextReasoningContent(string.Empty) { ProtectedData = "BLOB" },
            new TextContent("上次的答案"),
        ]),
        new(ChatRole.User, "追问"),
    ];

    [Fact]
    public async Task Request_UsesInputItemArray_NotMessages()
    {
        JsonNode? body = await SendBodyAsync(HistoryWithTicket());

        Assert.NotNull(body);
        Assert.NotNull(body["input"]);           // item 数组
        Assert.Null(body["messages"]);           // 不能是 chat 的形状
        Assert.Equal("message", body["input"]![0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Request_AsksForEncryptedContent()
    {
        JsonNode? body = await SendBodyAsync(HistoryWithTicket());

        Assert.NotNull(body);
        JsonArray? include = body["include"] as JsonArray;
        Assert.NotNull(include);
        Assert.Contains(include, x => x!.GetValue<string>() == "reasoning.encrypted_content");
    }

    /// <summary>
    /// <c>store</c> 必须是 false：开了它服务端就存我们的响应，
    /// 密文在服务端而不在客户端手里——那就不是「客户端保管、原样带回」了。
    /// </summary>
    [Fact]
    public async Task Request_DoesNotStoreServerSide()
    {
        JsonNode? body = await SendBodyAsync(HistoryWithTicket());

        Assert.NotNull(body);
        Assert.False(body["store"]!.GetValue<bool>());
    }

    /// <summary>
    /// ADR 0100 的核心不变量：带密文的思考内容原样往返，字节不变。
    /// 密文被重新编码就作废——所以这里比的是逐字节相同，不是「解出来一样」。
    /// </summary>
    [Fact]
    public async Task Request_RoundTripsEncryptedContent_ByteForByte()
    {
        const string blob = "Q-PaD+/=abc123密文部分";
        List<ChatMessage> messages =
        [
            new(ChatRole.User, "问题"),
            new(ChatRole.Assistant,
            [
                new TextReasoningContent(string.Empty) { ProtectedData = blob },
                new TextContent("上次的答案"),
            ]),
        ];

        JsonNode? body = await SendBodyAsync(messages);

        Assert.NotNull(body);
        JsonArray input = (JsonArray)body["input"]!;
        JsonNode? reasoning = input.FirstOrDefault(x => x!["type"]!.GetValue<string>() == "reasoning");
        Assert.NotNull(reasoning);
        Assert.Equal(blob, reasoning!["encrypted_content"]!.GetValue<string>());
    }

    /// <summary>
    /// reasoning 是<b>独立的一条 item</b>，插在原来的位置，不占助手消息的位置。
    /// 这是 ADR 0100「位置不漂移」在 responses 线上的实现方式——
    /// chat 侧靠数第几条助手消息，responses 侧靠 item 本身的顺序。
    /// </summary>
    [Fact]
    public async Task Request_KeepsReasoningItemInPlace()
    {
        JsonNode? body = await SendBodyAsync(HistoryWithTicket());

        Assert.NotNull(body);
        JsonArray input = (JsonArray)body["input"]!;
        var types = input.Select(x => x!["type"]!.GetValue<string>()).ToList();

        Assert.Equal(["message", "reasoning", "message", "message"], types);
        // reasoning 夹在第一条 user 与 assistant 之间——原位
        Assert.Equal("user", input[0]!["role"]!.GetValue<string>());
        Assert.Equal("assistant", input[2]!["role"]!.GetValue<string>());
    }

    // ---------- 门禁（换模型后旧票这一轮不带） ----------

    /// <summary>
    /// 换模型后，别的模型签的票这一轮<b>不能</b>带过去。
    ///
    /// responses 线的密文是 <b>SDK 自己从消息里读</b>的（<c>ProtectedData</c> →
    /// <c>encrypted_content</c>），不经过 chat 线那道「按序号收集」——门禁若只长在那儿，
    /// 这条线上的门禁就是空的。而网关<b>不校验归属</b>（实测照样 200），
    /// 发错了不会报错，只会安静地错。
    /// </summary>
    [Fact]
    public async Task Request_DoesNotCarryAnotherModelsTicket()
    {
        JsonNode? body = await SendBodyViaLazyAsync(HistoryWithTicket("别的供应商", "别的模型"));

        Assert.NotNull(body);
        JsonArray input = (JsonArray)body["input"]!;
        // 密文不能过去。空思考块也不能留——responses 侧那是一条没有
        // encrypted_content 的 reasoning item，各协议都不收
        Assert.DoesNotContain(input, x => x!["type"]!.GetValue<string>() == "reasoning");
    }

    /// <summary>
    /// 自己家的票<b>要</b>带过去——门禁不能误伤。
    /// </summary>
    [Fact]
    public async Task Request_CarriesItsOwnModelsTicket()
    {
        JsonNode? body = await SendBodyViaLazyAsync(HistoryWithTicket(OwnProvider, OwnModel));

        Assert.NotNull(body);
        JsonArray input = (JsonArray)body["input"]!;
        JsonNode? reasoning = input.FirstOrDefault(x => x!["type"]!.GetValue<string>() == "reasoning");
        Assert.NotNull(reasoning);
        Assert.Equal("BLOB", reasoning!["encrypted_content"]!.GetValue<string>());
    }

    private const string OwnProvider = nameof(RemoteModelConfig);
    private const string OwnModel = "m";

    /// <summary>
    /// 走完整的客户端链路——门禁长在 <c>LazyChatClient</c> 那一层，
    /// 直连协议客户端是绕过去的（那样测不到门禁）。
    /// </summary>
    private static async Task<JsonNode?> SendBodyViaLazyAsync(List<ChatMessage> messages)
    {
        var stub = new StubHandler();
        var httpHandler = new OpenAICompatibleHttpHandler("https://api.example.com/v1/responses", stub);
        // 必须是 responses 格式的模型：客户端按模型声明的格式分流，
        // 地址只决定打哪个端点
        var config = new RemoteModelConfig { ModelName = "responses-test", ModelId = "m", ApiFormat = EApiFormat.Responses };
        RemoteModelInfo remote = new() { Config = config };
        ModelRunningData data = new(remote);
        data.CompleteLoading(
            OpenAICompatibleChatClient.Create(httpHandler, config, "m", "key"),
            loadToken: CancellationToken.None);

        try
        {
            await new LazyChatClient(() => data).GetResponseAsync(messages);
        }
        catch
        {
            // 响应形状不被接受没关系——要看的是请求体
        }

        return stub.LastBody is null ? null : JsonNode.Parse(stub.LastBody);
    }
    private static List<ChatMessage> HistoryWithTicket(string provider, string model)
    {
        TextReasoningContent reasoning = new(string.Empty) { ProtectedData = "BLOB" };
        reasoning.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        reasoning.AdditionalProperties[ReasoningTicketCarrier.ProducerProviderKey] = provider;
        reasoning.AdditionalProperties[ReasoningTicketCarrier.ProducerModelKey] = model;

        return
        [
            new(ChatRole.User, "问题"),
            new(ChatRole.Assistant, [reasoning, new TextContent("上次的答案")]),
            new(ChatRole.User, "追问"),
        ];
    }
}
