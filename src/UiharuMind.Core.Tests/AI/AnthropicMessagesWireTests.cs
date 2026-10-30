using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.AI.Net.Request;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.AI.Reasoning;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// Anthropic Messages 线（<c>/v1/messages</c>）的接线与票据往返。
///
/// 这条线与另两条的差别全在协议形状上，钉住四件事：
/// 1. 地址归一：填 <c>/v1</c>、填根、填完整路径都收敛到 <c>/v1/messages</c>
/// 2. 请求体是 Anthropic 的形状（顶层 <c>system</c>、<c>content</c> 是 block 数组）
/// 3. <b>签名与思考正文必须合成一个 thinking 块</b>——Anthropic 的流式响应把两者拆成两帧发，
///    官方 SDK 原样交给两条思考内容，不合成就会发出两个块、头一个签名是空串，服务端 400
/// 4. 票据仍然落在 <see cref="TextReasoningContent.ProtectedData"/> 里（ADR 0100 的落盘载体没变）
/// </summary>
public class AnthropicMessagesWireTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public Uri? LastUri { get; private set; }

        public string Response = """
        {"id":"msg_1","type":"message","role":"assistant","model":"m",
         "content":[{"type":"thinking","thinking":"思考正文","signature":"SIG123"},
                    {"type":"text","text":"最终回答"}],
         "stop_reason":"end_turn","stop_sequence":null,
         "usage":{"input_tokens":10,"output_tokens":20}}
        """;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri;
            if (request.Content != null)
                LastBody = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Response, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (WireHttpHandler Http, StubHandler Stub) Build(string address = "https://api.example.com/v1/messages")
    {
        StubHandler stub = new();
        return (new WireHttpHandler(address, stub), stub);
    }

    private static IChatClient Client(WireHttpHandler http) =>
        ChatClientFactory.Create(http, new RemoteModelConfig { ModelId = "m", ApiFormat = EApiFormat.Messages },
            "m", "key");

    // ---------- 地址归一 ----------

    [Theory]
    [InlineData("https://api.anthropic.com/v1", "/v1/messages")]
    [InlineData("https://api.anthropic.com", "/v1/messages")]
    [InlineData("https://api.anthropic.com/v1/messages", "/v1/messages")]
    public void ResolveMessages_ConvergesToTheSameEndpoint(string address, string expected)
    {
        UriBuilder builder = WireProtocols.Messages.Resolve(address);
        Assert.Equal(expected, builder.Uri.AbsolutePath);
    }

    /// <summary>
    /// 请求必须打在 <c>/v1/messages</c> 上。
    ///
    /// <b>地址要填到端点那一层</b>：格式是按地址判的（handler 按端点长期存活，
    /// 地址才是它实际要打的东西），跟 responses 线一样——填 <c>/v1</c> 会被当成 chat 线。
    /// </summary>
    [Fact]
    public async Task Request_GoesToMessagesEndpoint()
    {
        var (http, stub) = Build("https://api.example.com/v1/messages");
        try { await Client(http).GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]); } catch { }

        Assert.NotNull(stub.LastUri);
        Assert.Equal("/v1/messages", stub.LastUri!.AbsolutePath);
    }

    /// <summary>
    /// 填了 <c>/v1/messages</c> 的地址不能被再补一层——否则会打到 <c>/v1/messages/v1/messages</c>。
    /// </summary>
    [Fact]
    public async Task Request_DoesNotDoubleThePath()
    {
        var (http, stub) = Build("https://api.example.com/v1/messages");
        try { await Client(http).GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]); } catch { }

        Assert.NotNull(stub.LastUri);
        Assert.Equal("/v1/messages", stub.LastUri!.AbsolutePath);
    }

    // ---------- 请求体形状 ----------

    /// <summary>
    /// 请求体是 Anthropic 的形状：<c>content</c> 是 block 数组，不是 chat 的字符串数组。
    /// </summary>
    [Fact]
    public async Task Request_UsesBlockArrayContent()
    {
        var (http, stub) = Build();
        try { await Client(http).GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]); } catch { }

        Assert.NotNull(stub.LastBody);
        JsonNode? body = JsonNode.Parse(stub.LastBody!);
        JsonArray? messages = body?["messages"] as JsonArray;
        Assert.NotNull(messages);
        Assert.Equal("text", messages![0]!["content"]![0]!["type"]!.GetValue<string>());
    }

    // ---------- 思考块归一（这条线最容易翻车的地方） ----------

    /// <summary>
    /// 流式下 Anthropic 把思考正文与签名拆成两帧发，SDK 交成两条思考内容。
    /// 这两条回到请求里时<b>必须合成一个 thinking 块</b>——分成两个块的话，
    /// 头一个的 <c>signature</c> 是空串，Anthropic 拒收。
    /// </summary>
    [Fact]
    public async Task Request_MergesThinkingTextAndSignatureIntoOneBlock()
    {
        var (http, stub) = Build();
        List<ChatMessage> history =
        [
            new(ChatRole.User, "问题"),
            new(ChatRole.Assistant,
            [
                new TextReasoningContent("思考正文"),
                new TextReasoningContent(string.Empty) { ProtectedData = "SIG123" },
                new TextContent("上次的答案"),
            ]),
            new(ChatRole.User, "追问"),
        ];

        try { await Client(http).GetResponseAsync(history); } catch { }

        Assert.NotNull(stub.LastBody);
        JsonNode? body = JsonNode.Parse(stub.LastBody!);
        JsonArray messages = (JsonArray)body!["messages"]!;
        JsonNode assistant = messages[1]!;

        JsonArray content = (JsonArray)assistant["content"]!;
        var thinkingBlocks = content.Where(x => x!["type"]!.GetValue<string>() == "thinking").ToList();

        // 只有一个 thinking 块，且签名不为空
        Assert.Single(thinkingBlocks);
        Assert.Equal("SIG123", thinkingBlocks[0]!["signature"]!.GetValue<string>());
        Assert.Equal("思考正文", thinkingBlocks[0]!["thinking"]!.GetValue<string>());
    }

    /// <summary>
    /// 已经合成过的一条（有正文 + 有签名）不该被二次合成——
    /// 幂等是必须的，否则第二轮请求体与第一轮不同，前缀就分叉了。
    /// </summary>
    [Fact]
    public async Task Request_IsIdempotentOnAlreadyMergedThinking()
    {
        var (http, stub) = Build();
        List<ChatMessage> history =
        [
            new(ChatRole.User, "问题"),
            new(ChatRole.Assistant,
            [
                new TextReasoningContent("思考正文") { ProtectedData = "SIG123" },
                new TextContent("上次的答案"),
            ]),
        ];

        try { await Client(http).GetResponseAsync(history); } catch { }

        Assert.NotNull(stub.LastBody);
        JsonNode? body = JsonNode.Parse(stub.LastBody!);
        JsonArray content = (JsonArray)((JsonArray)body!["messages"]!)[1]!["content"]!;
        var thinkingBlocks = content.Where(x => x!["type"]!.GetValue<string>() == "thinking").ToList();

        Assert.Single(thinkingBlocks);
        Assert.Equal("SIG123", thinkingBlocks[0]!["signature"]!.GetValue<string>());
    }

    /// <summary>
    /// 只有签名没有正文的思考（Anthropic 的 <c>redacted_thinking</c> 形态）不能被并走——
    /// 它自己就是一个块，并进别处就发不出去了；而且必须<b>还原成它自己</b>：
    /// 官方 SDK 靠 <c>RawRepresentation</c> 认出这一形态，而那是 provider 的原始对象引用，
    /// 落盘往返后就没了，不补回去就发成「正文为空的普通 thinking 块」，与服务端给的原块对不上。
    /// </summary>
    [Fact]
    public async Task Request_KeepsSignatureOnlyThinkingAsRedactedBlock()
    {
        var (http, stub) = Build();
        List<ChatMessage> history =
        [
            new(ChatRole.User, "问题"),
            new(ChatRole.Assistant,
            [
                new TextReasoningContent(string.Empty) { ProtectedData = "REDDATA" },
                new TextContent("上次的答案"),
            ]),
        ];

        try { await Client(http).GetResponseAsync(history); } catch { }

        Assert.NotNull(stub.LastBody);
        JsonNode? body = JsonNode.Parse(stub.LastBody!);
        JsonArray content = (JsonArray)((JsonArray)body!["messages"]!)[1]!["content"]!;

        JsonNode? redacted = content.FirstOrDefault(x => x!["type"]!.GetValue<string>() == "redacted_thinking");
        Assert.NotNull(redacted);
        Assert.Equal("REDDATA", redacted!["data"]!.GetValue<string>());
    }

    // ---------- 响应侧 ----------

    /// <summary>
    /// 响应里的 thinking 与 signature 落到 <see cref="TextReasoningContent"/> 上：
    /// 正文在 <c>Text</c>、签名在 <c>ProtectedData</c>——与 ADR 0100 定的落盘载体同一个字段。
    /// </summary>
    [Fact]
    public async Task Response_SplitsThinkingTextAndSignatureOntoProtectedData()
    {
        var (http, _) = Build();
        ChatResponse response = await Client(http).GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        TextReasoningContent? reasoning = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>()
            .FirstOrDefault();

        Assert.NotNull(reasoning);
        Assert.Equal("思考正文", reasoning!.Text);
        Assert.Equal("SIG123", reasoning.ProtectedData);
    }

    // ---------- 归一器本身 ----------

    /// <summary>
    /// 归一器不动原文：产方身份（门禁的输入）跟着走，不能在合并时丢掉。
    /// </summary>
    [Fact]
    public void Normalize_KeepsProducerIdentityOnMergedThinking()
    {
        TextReasoningContent text = new("思考正文");
        text.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        text.AdditionalProperties["_ticket_provider"] = "prov";
        TextReasoningContent cipher = new(string.Empty) { ProtectedData = "SIG" };

        IReadOnlyList<ChatMessage> result = AnthropicThinkingNormalizer.Normalize(
        [
            new(ChatRole.Assistant, [text, cipher]),
        ]);

        TextReasoningContent merged = result[0].Contents.OfType<TextReasoningContent>().Single();
        Assert.Equal("思考正文", merged.Text);
        Assert.Equal("SIG", merged.ProtectedData);
        Assert.Equal("prov", merged.AdditionalProperties!["_ticket_provider"]!.ToString());
    }

    /// <summary>
    /// 没有要合并的地方时，一个字节都不该动——返回的就是入参那一份。
    /// </summary>
    [Fact]
    public void Normalize_LeavesUnrelatedMessagesAlone()
    {
        List<ChatMessage> source =
        [
            new(ChatRole.User, "问题"),
            new(ChatRole.Assistant, [new TextContent("答案")]),
        ];

        IReadOnlyList<ChatMessage> result = AnthropicThinkingNormalizer.Normalize(source);

        Assert.Same(source, result);
    }

    /// <summary>
    /// 归一器是<b>Anthropic 专用的</b>：它认的是 Anthropic 的形状，
    /// 「正文为空 + 有密文」在��条线上就是脱敏思考（会还原成 <c>redacted_thinking</c> 块）。
    /// chat 线的密文形态（整块 <c>reasoning_details</c>）走的是另一条路，根本不经过这里。
    /// </summary>
    [Fact]
    public void Normalize_RestoresRedactedThinkingShape()
    {
        List<ChatMessage> source =
        [
            new(ChatRole.Assistant,
            [
                new TextReasoningContent(string.Empty) { ProtectedData = "REDDATA" },
                new TextContent("答案"),
            ]),
        ];

        IReadOnlyList<ChatMessage> result = AnthropicThinkingNormalizer.Normalize(source);

        TextReasoningContent reasoning = result[0].Contents.OfType<TextReasoningContent>().Single();
        Assert.Equal("REDDATA", reasoning.ProtectedData);
        // 原始表示被装回去了——官方 SDK 靠它认出这是脱敏思考
        Assert.NotNull(reasoning.RawRepresentation);
    }

    // ---------- 门禁（换模型后旧票这一轮不带） ----------

    /// <summary>
    /// 换模型后，别的模型签的票这一轮<b>不能</b>带过去。
    ///
    /// 这条线上的密文是 <b>SDK 自己从消息里读</b>的（<c>ProtectedData</c> → <c>signature</c>），
    /// 不经过 chat 线那道「按序号收集」——所以门禁若只长在那儿，这条线上的门禁就是空的。
    /// 而网关<b>不校验归属</b>（实测照样 200），发错了不会报错，只会安静地错。
    ///
    /// <para>票被摘掉后那条思考就成了「有正文、无签名」——Anthropic 拒收这种块，
    /// 所以它整个不该出现在请求里。</para>
    /// </summary>
    [Fact]
    public async Task Request_DoesNotCarryAnotherModelsTicket()
    {
        var (http, stub) = Build();
        List<ChatMessage> history =
        [
            new(ChatRole.User, "问题"),
            new(ChatRole.Assistant,
            [
                ForeignTicket(),
                new TextContent("上次的答案"),
            ]),
            new(ChatRole.User, "追问"),
        ];

        await Lazy(http).GetResponseAsync(history);

        Assert.NotNull(stub.LastBody);
        JsonNode? body = JsonNode.Parse(stub.LastBody!);
        JsonArray content = (JsonArray)((JsonArray)body!["messages"]!)[1]!["content"]!;

        Assert.DoesNotContain(content, x => x!["type"]!.GetValue<string>() == "thinking");
    }

    /// <summary>
    /// 自己家的票<b>要</b>带过去——门禁不能误伤。
    /// </summary>
    [Fact]
    public async Task Request_CarriesItsOwnModelsTicket()
    {
        var (http, stub) = Build();
        List<ChatMessage> history =
        [
            new(ChatRole.User, "问题"),
            new(ChatRole.Assistant,
            [
                OwnTicket(),
                new TextContent("上次的答案"),
            ]),
            new(ChatRole.User, "追问"),
        ];

        await Lazy(http).GetResponseAsync(history);

        Assert.NotNull(stub.LastBody);
        JsonNode? body = JsonNode.Parse(stub.LastBody!);
        JsonArray content = (JsonArray)((JsonArray)body!["messages"]!)[1]!["content"]!;

        JsonNode? thinking = content.FirstOrDefault(x => x!["type"]!.GetValue<string>() == "thinking");
        Assert.NotNull(thinking);
        Assert.Equal("OWN", thinking!["signature"]!.GetValue<string>());
    }

    /// <summary>产出方是别家的票</summary>
    private static TextReasoningContent ForeignTicket()
    {
        TextReasoningContent reasoning = new("思考正文") { ProtectedData = "FOREIGN" };
        reasoning.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        reasoning.AdditionalProperties[ReasoningTicketCarrier.ProducerProviderKey] = "别的供应商";
        reasoning.AdditionalProperties[ReasoningTicketCarrier.ProducerModelKey] = "别的模型";
        return reasoning;
    }

    /// <summary>产出方就是本测试那个模型的票</summary>
    private static TextReasoningContent OwnTicket()
    {
        TextReasoningContent reasoning = new("思考正文") { ProtectedData = "OWN" };
        reasoning.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        reasoning.AdditionalProperties[ReasoningTicketCarrier.ProducerProviderKey] = nameof(RemoteAgnesModelConfig);
        reasoning.AdditionalProperties[ReasoningTicketCarrier.ProducerModelKey] = "m";
        return reasoning;
    }

    /// <summary>走完整的客户端链路（门禁在 <c>LazyChatClient</c> 那一层，不在协议客户端里）</summary>
    private static LazyChatClient Lazy(WireHttpHandler http)
    {
        RemoteModelInfo remote = new()
        {
            Config = new RemoteAgnesModelConfig { ModelName = "anthropic-test", ModelId = "m" },
        };
        ModelRunningData data = new(remote);
        data.CompleteLoading(Client(http), loadToken: CancellationToken.None);
        return new LazyChatClient(() => data);
    }

    // ---------- 思考参数 ----------

    /// <summary>
    /// 开了思考档位就要写 <c>thinking</c>，而且是 Anthropic 的形状：
    /// <c>{type: enabled, budget_tokens: N}</c>——一个 token 预算，不是档位名。
    /// </summary>
    [Theory]
    [InlineData("low", 2048)]
    [InlineData("medium", 8192)]
    [InlineData("high", 16384)]
    public async Task Request_WritesThinkingBudget(string level, int expectedBudget)
    {
        var (client, stub) = ThinkingClient(level);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        JsonNode? thinking = ReadThinking(stub);
        Assert.NotNull(thinking);
        Assert.Equal("enabled", thinking!["type"]!.GetValue<string>());
        Assert.Equal(expectedBudget, thinking["budget_tokens"]!.GetValue<int>());
    }

    /// <summary>
    /// 显式关闭思考写 <c>disabled</c>。
    /// </summary>
    [Fact]
    public async Task Request_WritesDisabledThinking()
    {
        var (client, stub) = ThinkingClient("none");
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        JsonNode? thinking = ReadThinking(stub);
        Assert.NotNull(thinking);
        Assert.Equal("disabled", thinking!["type"]!.GetValue<string>());
    }

    /// <summary>
    /// 「不干预」就<b>不写</b> <c>thinking</c>——与另外两条线同一口径：
    /// 没选档位就别替模型做决定（Anthropic 的默认是不思考）。
    /// </summary>
    [Fact]
    public async Task Request_OmitsThinkingWhenAuto()
    {
        var (client, stub) = ThinkingClient(string.Empty);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Null(ReadThinking(stub));
    }

    /// <summary>
    /// 思考预算必须严格小于输出预算——装不下就不写 <c>thinking</c>，
    /// 发一个服务端会拒的组合比不思考更糟。
    /// </summary>
    [Fact]
    public async Task Request_OmitsThinkingWhenBudgetDoesNotFit()
    {
        // 输出预算 1024，装不下 Anthropic 的最小思考预算（也是 1024，必须严格小于）
        var (client, stub) = ThinkingClient("low", maxTokens: 1024);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Null(ReadThinking(stub));
    }

    private static JsonNode? ReadThinking(StubHandler stub)
    {
        Assert.NotNull(stub.LastBody);
        JsonNode? body = JsonNode.Parse(stub.LastBody!);
        return body?["thinking"];
    }

    /// <summary>带思考档位的客户端（不经协议无关的 HTTP 层——这里只看思考参数）</summary>
    private static (IChatClient Client, StubHandler Stub) ThinkingClient(string level, int maxTokens = 32000)
    {
        var stub = new StubHandler();
        var handler = new AnthropicThinkingHandler(level, maxTokens, stub);
        var options = new Anthropic.Core.ClientOptions
        {
            ApiKey = "key",
            HttpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
        };
        return (new Anthropic.AnthropicClient(options).AsIChatClient("m", maxTokens), stub);
    }
}
