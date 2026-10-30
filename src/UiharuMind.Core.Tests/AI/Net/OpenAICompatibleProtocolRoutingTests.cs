using System.Net;
using System.Text;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.AI.Net.Transport;

namespace UiharuMind.Core.Tests.AI.Net;

/// <summary>
/// handler 按地址分流：填了 <c>/responses</c> 就打 responses 端点，
/// 填 chat 就打 chat 端点——两条线不能串。
///
/// 判据是地址本身（handler 按端点长期存活，地址才是它实际要打的东西），
/// 不是模型声明的格式。
/// </summary>
public class OpenAICompatibleProtocolRoutingTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Ok(string body, string mediaType = "application/json") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    [Fact]
    public async Task ResponsesAddress_HitsTheResponsesEndpoint()
    {
        Uri? hit = null;
        var handler = new OpenAICompatibleHttpHandler(
            "https://api.example.com/v1/responses",
            new StubHandler(request =>
            {
                hit = request.RequestUri;
                return Ok("{}");
            }));

        using var client = new HttpClient(handler);
        await client.GetAsync("https://api.example.com/v1/chat/completions");

        Assert.Equal("https://api.example.com/v1/responses", hit?.ToString());
    }

    [Fact]
    public async Task ChatAddress_HitsTheChatEndpoint()
    {
        Uri? hit = null;
        var handler = new OpenAICompatibleHttpHandler(
            "https://api.example.com/v1/chat/completions",
            new StubHandler(request =>
            {
                hit = request.RequestUri;
                return Ok("{}");
            }));

        using var client = new HttpClient(handler);
        await client.GetAsync("https://api.example.com/v1/responses");

        Assert.Equal("https://api.example.com/v1/chat/completions", hit?.ToString());
    }

    /// <summary>
    /// 根路径按 chat 线走（默认）：<c>/v1</c> 与空路径都收敛到 chat completions，
    /// 不是 responses——否则老配置会被悄悄改打另一个端点。
    /// </summary>
    [Fact]
    public async Task RootAddress_StaysOnTheChatLine()
    {
        Uri? hit = null;
        var handler = new OpenAICompatibleHttpHandler(
            "https://api.example.com/v1",
            new StubHandler(request =>
            {
                hit = request.RequestUri;
                return Ok("{}");
            }));

        using var client = new HttpClient(handler);
        await client.GetAsync("https://api.example.com/v1/whatever");

        Assert.Equal("https://api.example.com/v1/chat/completions", hit?.ToString());
    }

    /// <summary>
    /// Anthropic 线：填了 <c>/messages</c> 就打 messages 端点——
    /// 不能落到 chat 那条路上（那会把请求发到一个只认 <c>messages</c> 的服务上）。
    /// </summary>
    [Fact]
    public async Task MessagesAddress_HitsTheMessagesEndpoint()
    {
        Uri? hit = null;
        var handler = new OpenAICompatibleHttpHandler(
            "https://api.example.com/v1/messages",
            new StubHandler(request =>
            {
                hit = request.RequestUri;
                return Ok("{}");
            }));

        using var client = new HttpClient(handler);
        await client.GetAsync("https://api.example.com/v1/chat/completions");

        Assert.Equal("https://api.example.com/v1/messages", hit?.ToString());
    }

    /// <summary>
    /// 三条线的判据互不重叠：<c>/responses</c> 的地址不能被当成 messages，
    /// <c>/messages</c> 的地址也不能被当成 responses。
    /// </summary>
    [Theory]
    [InlineData("https://api.example.com/v1/responses", EApiFormat.Responses)]
    [InlineData("https://api.example.com/v1/messages", EApiFormat.Messages)]
    [InlineData("https://api.example.com/v1/chat/completions", EApiFormat.ChatCompletions)]
    public void EndpointPath_IdentifiesTheWireFormat(string address, EApiFormat expected)
    {
        var builder = new UriBuilder(address);
        EApiFormat actual = ChatCompletionUri.IsMessages(builder.Uri)
            ? EApiFormat.Messages
            : ChatCompletionUri.IsResponses(builder.Uri) ? EApiFormat.Responses : EApiFormat.ChatCompletions;

        Assert.Equal(expected, actual);
    }
}
