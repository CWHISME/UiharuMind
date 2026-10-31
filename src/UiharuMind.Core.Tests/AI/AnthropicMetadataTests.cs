using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.AI.Net.Request;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// Anthropic 线的 <c>metadata.user_id</c>：这是那条路径上的<b>会话亲和信号</b>——
/// Claude Code 就把会话 UUID 放在这里，兼容 Claude Code 的网关按它挑上游副本。
///
/// 值与亲和头<b>同源</b>：网关按第一个匹配的信号认会话，两处给不同值等于把自己分裂成两个会话。
/// 它只影响路由、不参与前缀内容，所以加它不会让缓存失效。
/// </summary>
public class AnthropicMetadataTests
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
                Content = new StringContent(
                    """{"id":"m","type":"message","role":"assistant","model":"m","content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":1,"output_tokens":1}}""",
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>把一段请求体过一遍处理器；<paramref name="affinity"/> 非空时模拟「本发有会话」</summary>
    private static async Task<string> RewriteAsync(string body, string? affinity, bool sendsAffinity = true,
        string? level = null)
    {
        CapturingHandler stub = new();
        using HttpClient client = new(new AnthropicRequestHandler(level, 32000,
            [ECacheBreakpoint.MessageLast], null, sendsAffinity, stub))
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        using LlmRequestScope.Scope scope = LlmRequestScope.Push(new LlmRequestContext { SessionAffinity = affinity });
        using HttpResponseMessage response = await client.PostAsync("https://api.example.com/v1/messages",
            new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return stub.Body;
    }

    /// <summary>有会话就写进去，值与亲和头同一个（这里用哈希形态的值）</summary>
    [Fact]
    public async Task Metadata_CarriesTheSessionAffinity()
    {
        const string body = """{"model":"m","messages":[{"role":"user","content":"问"}]}""";

        JsonNode json = JsonNode.Parse(await RewriteAsync(body, "0123456789abcdef0123456789abcdef"))!;

        Assert.Equal("0123456789abcdef0123456789abcdef", json["metadata"]!["user_id"]!.GetValue<string>());
    }

    /// <summary>没会话（能力预览、旁路探测）时不写：一个空串的 user_id 只会让网关把几发并成一个人</summary>
    [Fact]
    public async Task Metadata_SkippedWithoutASession()
    {
        const string body = """{"model":"m","messages":[{"role":"user","content":"问"}]}""";

        JsonNode json = JsonNode.Parse(await RewriteAsync(body, null))!;

        Assert.Null(json["metadata"]);
    }

    /// <summary>整层关掉时一个字段都不加（与亲和头同一个开关）</summary>
    [Fact]
    public async Task Metadata_SkippedWhenAffinityIsOff()
    {
        const string body = """{"model":"m","messages":[{"role":"user","content":"问"}]}""";

        JsonNode json = JsonNode.Parse(await RewriteAsync(body, "abc123", sendsAffinity: false))!;

        Assert.Null(json["metadata"]);
    }

    /// <summary>调用方自己给了 metadata 就不覆盖：那是显式配置，比我们推的优先</summary>
    [Fact]
    public async Task Metadata_DoesNotOverwriteAnExplicitOne()
    {
        const string body = """{"model":"m","metadata":{"user_id":"explicit"},"messages":[{"role":"user","content":"问"}]}""";

        JsonNode json = JsonNode.Parse(await RewriteAsync(body, "abc123"))!;

        Assert.Equal("explicit", json["metadata"]!["user_id"]!.GetValue<string>());
    }

    /// <summary>
    /// 走真实装配路径时它与断点、思考共存：三处都落在同一份请求体上，
    /// 根级新增拼成一段、块级标记各自插入
    /// </summary>
    [Fact]
    public async Task Metadata_CoexistsWithBreakpointsAndThinking()
    {
        CapturingHandler capture = new();
        IChatClient chat = ChatClientFactory.Create(new WireHttpHandler("https://api.example.com/v1/messages",
                capture, format: EApiFormat.Messages),
            new RemoteModelConfig { ModelId = "m", ApiFormat = EApiFormat.Messages, ThinkingEffort = "low" }, "m", "key");

        using LlmRequestScope.Scope scope = LlmRequestScope.Push(new LlmRequestContext { SessionAffinity = "abc123" });
        await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "问")],
            new ChatOptions { Instructions = "SYS" }, TestContext.Current.CancellationToken);

        JsonNode json = JsonNode.Parse(capture.Body)!;
        Assert.Equal("abc123", json["metadata"]!["user_id"]!.GetValue<string>());
        Assert.Equal("enabled", json["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal("ephemeral",
            json["messages"]![0]!["content"]![0]!["cache_control"]!["type"]!.GetValue<string>());
    }
}
