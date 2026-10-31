using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.AI.Net.Request;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// Anthropic 线的缓存断点：Anthropic 不会自动缓存，客户端必须在「希望缓存到此为止」的块上放
/// <c>cache_control</c>，否则这条线一个 token 都不命中。
///
/// 两段分开测：字节改写用<b>手写请求体</b>直接打（要覆盖裸字符串 content 这种适配层不会产出的形态），
/// 接线用 <see cref="ChatClientFactory"/> 走真实路径（断点清单来自 <see cref="RequestRewrite"/>）。
/// </summary>
public class AnthropicCacheBreakpointTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content != null) Body = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"m","type":"message","role":"assistant","model":"m","content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":1,"output_tokens":1}}""",
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>把一段手写请求体过一遍处理器，返回改写后的正文</summary>
    private static async Task<string> RewriteAsync(string body,
        IReadOnlyList<ECacheBreakpoint>? breakpoints = null, string? ttl = null, string? level = null,
        bool sendsAffinity = false)
    {
        CapturingHandler stub = new();
        using HttpClient client = new(new AnthropicRequestHandler(level, 32000,
            breakpoints ?? [ECacheBreakpoint.ToolLast, ECacheBreakpoint.MessageLast], ttl, sendsAffinity, stub))
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        using HttpResponseMessage response = await client.PostAsync("https://api.example.com/v1/messages",
            new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return stub.Body;
    }

    /// <summary>最后一个工具与最后一条消息的最后一个块各打一个断点，前面的一个都不动</summary>
    [Fact]
    public async Task Breakpoints_GoOnLastToolAndLastBlock()
    {
        const string body = """
            {"model":"m","max_tokens":100,
             "tools":[{"name":"a","input_schema":{}},{"name":"b","input_schema":{}}],
             "messages":[{"role":"user","content":[{"type":"text","text":"一"}]},
                         {"role":"assistant","content":[{"type":"text","text":"二"}]},
                         {"role":"user","content":[{"type":"tool_result","tool_use_id":"x","content":"结果"}]}]}
            """;

        JsonNode json = JsonNode.Parse(await RewriteAsync(body))!;
        JsonArray tools = (JsonArray)json["tools"]!;
        JsonArray messages = (JsonArray)json["messages"]!;

        Assert.Null(tools[0]!["cache_control"]); //只有最后一个工具带
        Assert.Equal("ephemeral", tools[1]!["cache_control"]!["type"]!.GetValue<string>());
        Assert.Null(messages[0]!["content"]![0]!["cache_control"]); //历史一个字节不动
        Assert.Equal("ephemeral",
            messages[2]!["content"]![0]!["cache_control"]!["type"]!.GetValue<string>());
    }

    /// <summary>
    /// 裸字符串 <c>content</c> 没地方挂键，得先包成块数组——只有最后一条会变形，
    /// 前面那条字符串消息保持原样
    /// </summary>
    [Fact]
    public async Task BareStringContent_IsWrappedOnlyOnTheLastMessage()
    {
        const string body = """
            {"model":"m","max_tokens":100,
             "messages":[{"role":"user","content":"第一问"},{"role":"user","content":"第二问"}]}
            """;

        JsonNode json = JsonNode.Parse(await RewriteAsync(body))!;
        JsonArray messages = (JsonArray)json["messages"]!;

        Assert.Equal("第一问", messages[0]!["content"]!.GetValue<string>()); //历史不动
        JsonArray wrapped = (JsonArray)messages[1]!["content"]!;
        Assert.Equal("text", wrapped[0]!["type"]!.GetValue<string>());
        Assert.Equal("第二问", wrapped[0]!["text"]!.GetValue<string>());
        Assert.Equal("ephemeral", wrapped[0]!["cache_control"]!["type"]!.GetValue<string>());
    }

    /// <summary>
    /// thinking 块上不能挂 <c>cache_control</c>：那种块服务端不认这个位置，
    /// 打在它上面等于白打——往前退到最近一个可打标的块
    /// </summary>
    [Fact]
    public async Task ThinkingBlock_IsSkippedInFavourOfThePrecedingText()
    {
        const string body = """
            {"model":"m","max_tokens":100,
             "messages":[{"role":"assistant","content":[
                {"type":"text","text":"答"},
                {"type":"thinking","thinking":"想","signature":"sig"}]}]}
            """;

        JsonNode json = JsonNode.Parse(await RewriteAsync(body))!;
        JsonArray content = (JsonArray)json["messages"]![0]!["content"]!;

        Assert.Equal("ephemeral", content[0]!["cache_control"]!["type"]!.GetValue<string>());
        Assert.Null(content[1]!["cache_control"]);
    }

    /// <summary>保留时长给到 1h 时，ttl 跟着断点一起发</summary>
    [Fact]
    public async Task LongRetention_CarriesTheTtl()
    {
        const string body = """{"model":"m","messages":[{"role":"user","content":"问"}]}""";

        JsonNode json = JsonNode.Parse(await RewriteAsync(body, ttl: CacheBreakpointSet.LongTtl))!;
        JsonNode marker = json["messages"]![0]!["content"]![0]!["cache_control"]!;

        Assert.Equal("ephemeral", marker["type"]!.GetValue<string>());
        Assert.Equal("1h", marker["ttl"]!.GetValue<string>());
    }

    /// <summary>没有工具、没有消息时改不动就不改：原样发出，不制造一个空壳请求体</summary>
    [Fact]
    public async Task NothingToMark_LeavesTheBodyAsIs()
    {
        const string body = """{"model":"m","max_tokens":100,"messages":[]}""";

        Assert.Equal(body, await RewriteAsync(body));
    }

    /// <summary>
    /// 关掉保留（<see cref="ECacheRetention.None"/>）时整条线一个断点都不打：
    /// 有些场景不希望缓存留痕，这时请求体必须与没这功能时逐字节相同
    /// </summary>
    [Fact]
    public async Task RetentionNone_SendsNoMarker()
    {
        CapturingHandler capture = new();
        IChatClient chat = ChatClientFactory.Create(new WireHttpHandler("https://api.example.com/v1/messages",
                capture, format: EApiFormat.Messages), new NoCacheModel { ModelId = "m", ApiFormat = EApiFormat.Messages }, "m", "key");

        await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "问")],
            new ChatOptions { Instructions = "SYS" }, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("cache_control", capture.Body);
    }

    /// <summary>
    /// 默认保留（1 小时）时走真实装配路径就该带上断点与 ttl——清单与寿命都来自 RequestRewrite，
    /// 不在这里另写一份
    /// </summary>
    [Fact]
    public async Task DefaultRetention_MarksThroughTheFactory()
    {
        CapturingHandler capture = new();
        IChatClient chat = ChatClientFactory.Create(new WireHttpHandler("https://api.example.com/v1/messages",
                capture, format: EApiFormat.Messages), new RemoteModelConfig { ModelId = "m", ApiFormat = EApiFormat.Messages }, "m", "key");

        await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "问")],
            new ChatOptions
            {
                Instructions = "SYS",
                Tools = [AIFunctionFactory.Create((string a) => a, "echo", "echo a")],
            }, TestContext.Current.CancellationToken);

        JsonNode json = JsonNode.Parse(capture.Body)!;
        JsonNode tool = json["tools"]![0]!["cache_control"]!;
        Assert.Equal("ephemeral", tool["type"]!.GetValue<string>());
        Assert.Equal("1h", tool["ttl"]!.GetValue<string>()); //默认就是 1 小时
        Assert.Equal("ephemeral",
            json["messages"]![0]!["content"]![0]!["cache_control"]!["type"]!.GetValue<string>());
    }

    /// <summary>5 分钟那档不带 ttl：ttl 缺省就是服务端默认寿命</summary>
    [Fact]
    public async Task ShortRetention_OmitsTheTtl()
    {
        const string body = """{"model":"m","messages":[{"role":"user","content":"问"}]}""";

        JsonNode json = JsonNode.Parse(await RewriteAsync(body, ttl: null))!;
        JsonNode marker = json["messages"]![0]!["content"]![0]!["cache_control"]!;

        Assert.Equal("ephemeral", marker["type"]!.GetValue<string>());
        Assert.Null(marker["ttl"]);
    }

    /// <summary>
    /// 思考参数与断点共存：<c>thinking</c> 追加在根对象末尾，断点在块里，两处互不影响——
    /// 这是最容易拼坏逗号的地方
    /// </summary>
    [Fact]
    public async Task ThinkingAndBreakpoints_Coexist()
    {
        const string body = """
            {"model":"m","max_tokens":32000,
             "messages":[{"role":"user","content":[{"type":"text","text":"问"}]}]}
            """;

        JsonNode json = JsonNode.Parse(await RewriteAsync(body, level: "high"))!;

        Assert.Equal("enabled", json["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal(16384, json["thinking"]!["budget_tokens"]!.GetValue<int>());
        Assert.Equal("ephemeral",
            json["messages"]![0]!["content"]![0]!["cache_control"]!["type"]!.GetValue<string>());
    }

    /// <summary>
    /// 空对象里不能插带前导逗号的标记——<c>{,"cache_control":…}</c> 不是合法 JSON，
    /// 整发请求会被服务端拒掉
    /// </summary>
    [Fact]
    public async Task EmptyToolObject_StillYieldsValidJson()
    {
        const string body = """{"model":"m","tools":[{}],"messages":[{"role":"user","content":"问"}]}""";

        JsonNode json = JsonNode.Parse(await RewriteAsync(body))!;

        Assert.Equal("ephemeral", json["tools"]![0]!["cache_control"]!["type"]!.GetValue<string>());
    }

    /// <summary><c>content</c> 是 null 时没地方挂标记，整发原样发出（不制造半个块）</summary>
    [Fact]
    public async Task NullContent_LeavesTheBodyAsIs()
    {
        const string body = """{"model":"m","messages":[{"role":"user","content":null}]}""";

        Assert.Equal(body, await RewriteAsync(body));
    }

    /// <summary>空工具数组不算「有工具」：不插标记，也不影响消息那一个断点</summary>
    [Fact]
    public async Task EmptyToolsArray_MarksOnlyTheMessage()
    {
        const string body = """{"model":"m","tools":[],"messages":[{"role":"user","content":"问"}]}""";

        JsonNode json = JsonNode.Parse(await RewriteAsync(body))!;

        Assert.Empty((JsonArray)json["tools"]!);
        Assert.Equal("ephemeral",
            json["messages"]![0]!["content"]![0]!["cache_control"]!["type"]!.GetValue<string>());
    }

    /// <summary>
    /// 缩进过的请求体、且 <c>tools</c> 排在 <c>messages</c> 之后：根级新增要落在最后一个属性后面，
    /// 块级标记要能跳过空白找到收尾花括号——两处都不能拼出多余的逗号
    /// </summary>
    [Fact]
    public async Task IndentedBodyWithToolsLast_StaysValid()
    {
        const string body = """
            {
              "model": "m",
              "messages": [
                {"role": "user", "content": [{"type": "text", "text": "问"}]}
              ],
              "tools": [
                {"name": "a"}
              ]
            }
            """;

        string rewritten = await RewriteAsync(body, level: "low");
        JsonNode json = JsonNode.Parse(rewritten)!; //解析不过就说明拼坏了

        Assert.Equal("ephemeral", json["tools"]![0]!["cache_control"]!["type"]!.GetValue<string>());
        Assert.Equal("ephemeral",
            json["messages"]![0]!["content"]![0]!["cache_control"]!["type"]!.GetValue<string>());
        Assert.Equal("enabled", json["thinking"]!["type"]!.GetValue<string>());
        Assert.DoesNotContain(",}", rewritten);
        Assert.DoesNotContain("{,", rewritten);
    }

    /// <summary>不打断点的模型（只测「关掉」这一路）</summary>
    private sealed class NoCacheModel : RemoteModelConfig
    {
        public override ECacheRetention CacheRetention => ECacheRetention.None;
    }
}
