using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.AI.Net.Response;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// Anthropic 线「写完思考参数之后请求还能不能发出去」。
///
/// 单独成一个文件，是因为前面那批测试都在 <c>StubHandler</c> 上直接读 <c>request.Content</c>，
/// 那条路绕过了真实传输层——而「正文被换掉」这件事正是在传输层被校验的：
/// 长度头留着旧值，<c>HttpClient</c> 会抛 <c>content would exceed Content-Length</c>，
/// 请求一个字节都出不去。断言放在这里才抓得到。
/// </summary>
public class AnthropicThinkingTransportTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public long? DeclaredLength { get; private set; }
        public int BodyLength { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content != null)
            {
                DeclaredLength = request.Content.Headers.ContentLength;
                byte[] bytes = await request.Content.ReadAsByteArrayAsync(ct);
                BodyLength = bytes.Length;
                Body = Encoding.UTF8.GetString(bytes);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static async Task<CapturingHandler> SendWithThinkingAsync(string level)
    {
        var stub = new CapturingHandler();
        var http = new WireHttpHandler("https://api.example.com/v1/messages", stub, format: EApiFormat.Messages);
        IChatClient client = ChatClientFactory.Create(
            http, new RemoteModelConfig { ModelId = "m", ApiFormat = EApiFormat.Messages, ThinkingEffort = level },
            "m", "key");
        try
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
        }
        catch
        {
            // 响应形状不被接受没关系——看的是请求体
        }

        return stub;
    }

    /// <summary>
    /// 换了正文之后，长度头必须与新正文一致。
    ///
    /// 曾经的错法是把旧正文的头整批搬过去，而 <c>Content-Length</c> 也在里面——于是
    /// 声明长度小于实际长度，<c>HttpClient</c> 拒绝发送（实测异常：
    /// <c>Unable to write content to request stream; content would exceed Content-Length.</c>）。
    /// 只要用户给 Anthropic 模型选了思考档位，这一发就必挂。
    /// </summary>
    [Theory]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("none")]
    public async Task ThinkingRewrite_KeepsDeclaredLengthEqualToBody(string level)
    {
        CapturingHandler stub = await SendWithThinkingAsync(level);

        Assert.NotNull(stub.DeclaredLength);
        Assert.Equal(stub.BodyLength, stub.DeclaredLength);
    }

    /// <summary>改写确实生效：<c>thinking</c> 在正文里，其余字段还在（不是把正文换成了半截）</summary>
    [Fact]
    public async Task ThinkingRewrite_ProducesCompleteJson()
    {
        CapturingHandler stub = await SendWithThinkingAsync("medium");

        Assert.Contains("\"thinking\"", stub.Body);
        Assert.Contains("\"model\"", stub.Body);
        // 能被解析成完整 JSON 就说明没被截断
        using var document = System.Text.Json.JsonDocument.Parse(stub.Body);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, document.RootElement.ValueKind);
    }

    /// <summary>
    /// 只有 chat 线的响应需要逐行修。responses 与 messages 的响应形状由各自 SDK 解析，
    /// 套上 chat 那层清洗器不只是白扫每行 JSON——<c>SseSanitizingStream</c> 还会因为
    /// 找不到 <c>data: [DONE]</c> 把正常收尾误报成「连接被掐」。
    /// </summary>
    [Theory]
    [InlineData(EApiFormat.Responses)]
    [InlineData(EApiFormat.Messages)]
    public async Task NonChatResponses_AreNotSanitized(EApiFormat format)
    {
        var sanitizer = new ResponseSanitizer(WireProtocols.For(format).SanitizesResponse, null);
        var original = new StringContent("data: {}\n\n", Encoding.UTF8, "text/event-stream");
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = original };

        HttpResponseMessage result = await sanitizer.SanitizeAsync(response, CancellationToken.None);

        Assert.Same(original, result.Content);
    }

    /// <summary>chat 线仍然要洗（否则这一层等于被关掉了）</summary>
    [Fact]
    public async Task ChatResponses_AreStillSanitized()
    {
        var sanitizer = new ResponseSanitizer(WireProtocols.Chat.SanitizesResponse, null);
        var original = new StringContent("data: {}\n\n", Encoding.UTF8, "text/event-stream");
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = original };

        HttpResponseMessage result = await sanitizer.SanitizeAsync(response, CancellationToken.None);

        Assert.IsType<SseSanitizingContent>(result.Content);
    }
}
