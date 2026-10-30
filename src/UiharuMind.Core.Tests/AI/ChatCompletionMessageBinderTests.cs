using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.AI.Net.Request;
using UiharuMind.Core.AI.Net.Transport;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 思考回填绑在<b>消息对象</b>上（ADR 0101）：<c>RawRepresentation</c> + <c>Patch</c>。
///
/// <para>这组测试要钉住的核心事实只有一条：<b>一条消息的思考只可能出现在它自己身上</b>。
/// 历史支持动态增删改，任何「第几条助手消息」的对位都会在增删之后错位，而错位不报错——
/// 服务端照收，只是把思考配到了别的消息上。绑定到对象之后，这类错配在结构上写不出来。</para>
/// </summary>
public class ChatCompletionMessageBinderTests
{
    private const string TicketA = """[{"type":"reasoning.encrypted","data":"AAA"}]""";

    /// <summary>把挂好思考的消息序列化成 wire JSON（SDK 自己的序列化器，不是我们拼的字符串）</summary>
    private static string Wire(ChatMessage message)
    {
        Assert.NotNull(message.RawRepresentation);
        return ModelReaderWriter.Write(message.RawRepresentation, new ModelReaderWriterOptions("W")).ToString();
    }

    private static JsonObject WireObject(ChatMessage message) => (JsonObject)JsonNode.Parse(Wire(message))!;

    private static ChatMessage Assistant(string text, string? reasoning = null, string? ticket = null) =>
        new(ChatRole.Assistant, Build(text, reasoning, ticket));

    private static List<AIContent> Build(string text, string? reasoning, string? ticket)
    {
        List<AIContent> contents = [];
        if (reasoning is not null || ticket is not null)
        {
            contents.Add(new TextReasoningContent(reasoning ?? string.Empty) { ProtectedData = ticket });
        }

        if (text.Length > 0) contents.Add(new TextContent(text));
        return contents;
    }

    private static ChatMessage Bind(ChatMessage message, string? reasoning, string? ticket, string field = "reasoning_content")
    {
        ChatMessage? bound = ChatCompletionMessageBinder.Attach(message, reasoning, ticket, field);
        Assert.NotNull(bound);
        return bound!;
    }

    // ── 绑定到对象 ──────────────────────────────────────────────

    /// <summary>
    /// 三条助手消息各带各的思考。这是本设计的全部要点：不需要任何外部映射表，
    /// 每条消息自己就知道该发什么。
    /// </summary>
    [Fact]
    public void EachMessage_CarriesItsOwnReasoning()
    {
        ChatMessage first = Bind(Assistant("a1"), "想一", null);
        ChatMessage second = Bind(Assistant("a2"), "想二", null);
        ChatMessage third = Bind(Assistant("a3"), "想三", null);

        Assert.Equal("想一", WireObject(first)["reasoning_content"]!.GetValue<string>());
        Assert.Equal("想二", WireObject(second)["reasoning_content"]!.GetValue<string>());
        Assert.Equal("想三", WireObject(third)["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>
    /// <b>历史增删之后仍然各归各位。</b>
    ///
    /// 旧做法按序号对位：删掉中间一条，后面每条都会取到上一条的思考；改一条（条数不变）
    /// 更是连数量闸门都拦不住。绑定到对象之后，这两种情况都不再是「对位失败」，而是根本不存在对位。
    /// </summary>
    [Fact]
    public void InsertingOrRemovingMessages_DoesNotShiftReasoning()
    {
        ChatMessage second = Assistant("a2", "想二");
        List<ChatMessage> history = [Assistant("a1", "想一"), second, Assistant("a3", "想三")];

        // 删掉第一条、在最前面插一条新的，再把剩下两条绑一遍
        history.RemoveAt(0);
        history.Insert(0, Assistant("插进来的", "插进来的思考"));

        ChatMessage rebound = Bind(history[1], "想二", null);

        Assert.Equal("想二", WireObject(rebound)["reasoning_content"]!.GetValue<string>());
        Assert.Equal("a2", WireObject(rebound)["content"]!.GetValue<string>());
    }

    /// <summary>票据与正文各自独立：同一条消息可能两样都有，也可能只有票（闭源模型没有正文）</summary>
    [Fact]
    public void TicketAndText_AreIndependent()
    {
        JsonObject both = WireObject(Bind(Assistant("a1"), "想一", TicketA));
        Assert.Equal("想一", both["reasoning_content"]!.GetValue<string>());
        Assert.Equal(TicketA, both["reasoning_details"]!.ToJsonString());

        JsonObject ticketOnly = WireObject(Bind(Assistant("a2"), null, TicketA));
        Assert.Null(ticketOnly["reasoning_content"]);
        Assert.Equal(TicketA, ticketOnly["reasoning_details"]!.ToJsonString());
    }

    /// <summary>思考键名跟模型走，不写死</summary>
    [Fact]
    public void ReasoningField_FollowsTheModel()
    {
        JsonObject message = WireObject(Bind(Assistant("a1"), "想一", null, "reasoning"));

        Assert.Equal("想一", message["reasoning"]!.GetValue<string>());
        Assert.Null(message["reasoning_content"]);
    }

    /// <summary>
    /// 历史里躺着的假票不发。旧版提取器会把工具参数数组当成票据存下来，
    /// 那些东西不会自己消失——回填前按结构再判一次。
    /// </summary>
    [Fact]
    public void BogusStoredTicket_IsNotSent()
    {
        JsonObject message = WireObject(Bind(Assistant("a1"), null, """[{"id":"c1","function":{"arguments":"{}"}}]"""));

        Assert.Null(message["reasoning_details"]);
    }

    /// <summary>不原地改历史里那条消息：持久对象里不该出现 SDK 的运行时引用</summary>
    [Fact]
    public void Attach_DoesNotTouchTheSourceMessage()
    {
        ChatMessage source = Assistant("a1", "想一");

        ChatMessage bound = Bind(source, "想一", null);

        Assert.Null(source.RawRepresentation);
        Assert.NotSame(source, bound);
        Assert.Equal("a1", source.Text);
    }

    /// <summary>
    /// 复刻不了的消息一律返回 null（交回 M.E.AI，这一轮不回填）。
    /// 附件有 M.E.AI 自己的一套映射，复刻不全就等于悄悄改内容。
    /// </summary>
    [Fact]
    public void UnsupportedMessageShapes_AreLeftToMeai()
    {
        ChatMessage withAttachment = new(ChatRole.Assistant,
            [new TextReasoningContent("想"), new DataContent(new byte[] { 1, 2, 3 }, "image/png")]);
        ChatMessage userMessage = new(ChatRole.User, "q");

        Assert.Null(ChatCompletionMessageBinder.Attach(withAttachment, "想", null, "reasoning_content"));
        Assert.Null(ChatCompletionMessageBinder.Attach(userMessage, "想", null, "reasoning_content"));
    }

    /// <summary>
    /// 作者名按 M.E.AI 的口径清洗。<b>本仓每条消息都带 <c>AuthorName</c></b>（按角色填人名），
    /// 所以这一步不能跳——跳过就等于 assistant 消息一律不接管，回填整个失效。
    /// 中文名清洗后为空，wire 上不出现 <c>name</c>；ASCII 名保留，非法字符删掉。
    /// </summary>
    [Fact]
    public void AuthorName_IsSanitizedLikeMeai()
    {
        JsonObject chinese = WireObject(Bind(new ChatMessage(ChatRole.Assistant, "a1") { AuthorName = "爱丽丝" }, "想", null));
        JsonObject ascii = WireObject(Bind(new ChatMessage(ChatRole.Assistant, "a1") { AuthorName = "alice-01" }, "想", null));

        Assert.Null(chinese["name"]);
        Assert.Equal("alice01", ascii["name"]!.GetValue<string>());
        Assert.Equal("想", chinese["reasoning_content"]!.GetValue<string>());
    }

    // ── 与 M.E.AI 的 wire 形状对齐 ────────────────────────────────

    /// <summary>
    /// 三种助手消息形状必须与 M.E.AI 的转换逐字节一致——差一个字节，这条消息的前缀缓存
    /// 就与上一轮对不上。期望值来自 M.E.AI 对同一批消息的实际序列化结果。
    /// </summary>
    [Fact]
    public void WireShape_MatchesMeai()
    {
        Assert.Equal("""{"role":"assistant","content":"你好","reasoning_content":"想"}""",
            Wire(Bind(Assistant("你好"), "想", null)));

        Assert.Equal(
            """{"role":"assistant","tool_calls":[{"id":"c1","type":"function","function":{"name":"Read","arguments":"{}"}}],"reasoning_content":"想"}""",
            Wire(Bind(ToolCallOnly(), "想", null)));

        Assert.Equal(
            """{"role":"assistant","content":"查一下","tool_calls":[{"id":"c1","type":"function","function":{"name":"Read","arguments":"{}"}}],"reasoning_content":"想"}""",
            Wire(Bind(TextAndToolCall(), "想", null)));
    }

    private static ChatMessage ToolCallOnly() =>
        new(ChatRole.Assistant, [new FunctionCallContent("c1", "Read", new Dictionary<string, object?>())]);

    private static ChatMessage TextAndToolCall() =>
        new(ChatRole.Assistant,
        [
            new TextContent("查一下"),
            new FunctionCallContent("c1", "Read", new Dictionary<string, object?>()),
        ]);

    // ── 端到端：真客户端 + 假服务端 ──────────────────────────────

    /// <summary>
    /// 端到端：思考真的进了请求体，而且落在它自己那条消息上。
    /// 这条走的是完整链路（绑定器 → M.E.AI → SDK → HTTP），不是只测对象。
    /// </summary>
    [Fact]
    public async Task RequestBody_CarriesReasoningOnItsOwnMessage()
    {
        var server = new CaptureServer();
        IChatClient client = ChatClientFactory.Create(
            new WireHttpHandler("http://localhost/v1", server), new ReplayModel(), "m", "key");

        await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.User, "q1"),
            Assistant("a1", "想一"),
            new ChatMessage(ChatRole.User, "q2"),
            Assistant("a2", "想二"),
        ], cancellationToken: TestContext.Current.CancellationToken);

        JsonArray messages = (JsonArray)JsonNode.Parse(server.Body!)!["messages"]!;
        Assert.Null(messages[0]!["reasoning_content"]);
        Assert.Equal("想一", messages[1]!["reasoning_content"]!.GetValue<string>());
        Assert.Null(messages[2]!["reasoning_content"]);
        Assert.Equal("想二", messages[3]!["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>
    /// <b>复刻一致性</b>：同一批消息，带思考（我们接管）与不带思考（M.E.AI 自己转）的请求体，
    /// 除思考字段外必须完全相同。
    ///
    /// 这是复刻转换的防漂移测试：M.E.AI 哪天改了 assistant 那一支的映射，这条就会红，
    /// 而不是等到线上前缀缓存莫名其妙掉下去才发现。
    /// </summary>
    [Fact]
    public async Task BoundBody_MatchesMeaiApartFromReasoningFields()
    {
        var plain = new CaptureServer();
        var bound = new CaptureServer();

        await ChatClientFactory.Create(new WireHttpHandler("http://localhost/v1", plain), new ReplayModel(), "m", "key")
            .GetResponseAsync(Conversation(withReasoning: false), cancellationToken: TestContext.Current.CancellationToken);
        await ChatClientFactory.Create(new WireHttpHandler("http://localhost/v1", bound), new ReplayModel(), "m", "key")
            .GetResponseAsync(Conversation(withReasoning: true), cancellationToken: TestContext.Current.CancellationToken);

        JsonArray expected = (JsonArray)JsonNode.Parse(plain.Body!)!["messages"]!;
        JsonArray actual = (JsonArray)JsonNode.Parse(bound.Body!)!["messages"]!;

        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            JsonObject message = (JsonObject)actual[i]!;
            message.Remove("reasoning_content");
            message.Remove("reasoning_details");
            Assert.True(JsonNode.DeepEquals(expected[i], message),
                $"第 {i} 条消息与 M.E.AI 的转换不一致：{expected[i]} vs {message}");
        }
    }

    private static List<ChatMessage> Conversation(bool withReasoning)
    {
        string? reasoning = withReasoning ? "想" : null;
        return
        [
            new ChatMessage(ChatRole.User, "q1"),
            // 本仓每条消息都带 AuthorName（按角色填人名），这条也带上，
            // 顺带把清洗口径的复刻一并压进逐字节比对
            new ChatMessage(ChatRole.Assistant, WithReasoning(reasoning,
            [
                new TextContent("查一下"),
                new FunctionCallContent("c1", "Read", new Dictionary<string, object?>()),
            ]))
            {
                AuthorName = "Alice 01",
            },
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "内容")]),
            new ChatMessage(ChatRole.Assistant, WithReasoning(reasoning, [new TextContent("收尾")]))
            {
                AuthorName = "爱丽丝",
            },
        ];
    }

    private static List<AIContent> WithReasoning(string? reasoning, List<AIContent> contents)
    {
        if (reasoning is null) return contents;
        contents.Insert(0, new TextReasoningContent(reasoning));
        return contents;
    }

    /// <summary>模型声明「自动回填」——接口默认是 false（显式不回传）</summary>
    private sealed class ReplayModel : ILlmModel
    {
        public string ModelName => "m";
        public string ModelPath => "";
        public bool IsVision => false;
        public string ModelDescription => "";
        public string ModelId => "m";
        public int Port => 0;
        public string ThinkingField => "reasoning_content";
        public bool? ReplayExistingReasoning => null;
    }

    /// <summary>记下请求体、回一份最小可用的 chat completion</summary>
    private sealed class CaptureServer : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            const string reply =
                """{"id":"c1","object":"chat.completion","created":0,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(reply, Encoding.UTF8, "application/json"),
            };
        }
    }
}
