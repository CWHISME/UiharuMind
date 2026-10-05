using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 请求体每次调用只改写一次：SDK 的重试在 HTTP 层之上，挂在重试之前才不会每撞一次 429 就把整份请求体重做一遍
/// </summary>
public class OpenAICompatibleRequestPolicyTests
{
    [Fact]
    public async Task Retries_ResendTheBodyRewrittenOnce()
    {
        int prepared = 0;
        var policy = new OpenAICompatibleRequestPolicy(json =>
        {
            prepared++;
            return json.Replace("\"a\":1", "\"a\":2", StringComparison.Ordinal);
        });
        var server = new FlakyServer(failures: 2);
        ClientPipeline pipeline = ClientPipeline.Create(
            new ClientPipelineOptions
            {
                Transport = new HttpClientPipelineTransport(new HttpClient(server)),
                RetryPolicy = new ImmediateRetryPolicy(),
            },
            perCallPolicies: [policy], perTryPolicies: ReadOnlySpan<PipelinePolicy>.Empty,
            beforeTransportPolicies: ReadOnlySpan<PipelinePolicy>.Empty);

        PipelineMessage message = pipeline.CreateMessage();
        message.Request.Method = "POST";
        message.Request.Uri = new Uri("http://localhost/v1/chat/completions");
        message.Request.Content = BinaryContent.Create(BinaryData.FromString("{\"a\":1}"));
        await pipeline.SendAsync(message);

        Assert.Equal(200, message.Response!.Status);
        Assert.Equal(1, prepared);
        Assert.Equal(["{\"a\":2}", "{\"a\":2}", "{\"a\":2}"], server.Bodies);
    }

    /// <summary>工厂建出的真客户端：撞两次 503 重试成功，请求体只记一次日志，三次发出的正文相同</summary>
    [Fact]
    public async Task ChatClient_LogsTheRequestBodyOnce_AcrossRetries()
    {
        string marker = "marker" + Guid.NewGuid().ToString("N");
        List<string> logged = [];
        void Handler(LogIndexEntry entry)
        {
            if (entry.Category == ELogCategory.LlmRequest) lock (logged) logged.Add(entry.Preview);
        }

        var server = new FlakyServer(failures: 2,
            "{\"id\":\"x\",\"object\":\"chat.completion\",\"created\":0,\"model\":\"m\"," +
            "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}");
        IChatClient client = OpenAICompatibleChatClient.Create(
            new OpenAICompatibleHttpHandler("http://localhost/v1", server), model: null, marker, "key",
            new ImmediateRetryPolicy());

        LogManager.Instance.OnLogAppended += Handler;
        try
        {
            ChatResponse response = await client.GetResponseAsync(marker, cancellationToken: TestContext.Current.CancellationToken);
            LogManager.Instance.Flush();

            Assert.Equal("ok", response.Text);
            Assert.Equal(3, server.Bodies.Count);
            Assert.Single(server.Bodies.Distinct());
            // 预览只有首行（正文在后面），按首行里的字符数认出本测试那条：日志是全局的，并行的别的测试也会记
            string head = $"OpenAI-compatible request ({server.Bodies[0].Length:N0} chars)";
            lock (logged) Assert.Single(logged, x => x.StartsWith(head, StringComparison.Ordinal));
        }
        finally
        {
            LogManager.Instance.OnLogAppended -= Handler;
        }
    }

    [Fact]
    public async Task NonPostRequests_AreLeftAlone()
    {
        int prepared = 0;
        var policy = new OpenAICompatibleRequestPolicy(json =>
        {
            prepared++;
            return json;
        });
        ClientPipeline pipeline = ClientPipeline.Create(
            new ClientPipelineOptions { Transport = new HttpClientPipelineTransport(new HttpClient(new FlakyServer(0))) },
            perCallPolicies: [policy], perTryPolicies: ReadOnlySpan<PipelinePolicy>.Empty,
            beforeTransportPolicies: ReadOnlySpan<PipelinePolicy>.Empty);

        PipelineMessage message = pipeline.CreateMessage();
        message.Request.Method = "GET";
        message.Request.Uri = new Uri("http://localhost/v1/models");
        await pipeline.SendAsync(message);

        Assert.Equal(0, prepared);
    }

    [Fact]
    public void Rewriter_ReturnsBodyUntouched_WhenNothingApplies()
    {
        const string json = "{\"model\":\"m\",\"messages\":[]}";

        Assert.Same(json, OpenAICompatibleRequestRewriter.Rewrite(json, model: null));
    }

    [Fact]
    public async Task Rewriter_ForbidsToolCalls_FromTheCallContext()
    {
        // AsyncLocal 只在这个异步流里生效，不漏到别的测试
        string rewritten = await Task.Run(() =>
        {
            LlmRequestContext.ForbidToolCalls = true;
            return OpenAICompatibleRequestRewriter.Rewrite("{\"model\":\"m\"}", model: null);
        }, TestContext.Current.CancellationToken);

        Assert.Equal("{\"model\":\"m\",\"tool_choice\":\"none\"}", rewritten);
    }

    /// <summary>
    /// 换下来的那份请求体要释放：SDK 序列化时现租的池化缓冲段只能靠它还，
    /// 而 message 释放时只会释放换上去的新正文
    /// </summary>
    [Fact]
    public async Task ReplacedBody_IsDisposed()
    {
        var body = new TrackingContent("{\"a\":1}");
        ClientPipeline pipeline = ClientPipeline.Create(
            new ClientPipelineOptions { Transport = new HttpClientPipelineTransport(new HttpClient(new FlakyServer(0))) },
            perCallPolicies: [new OpenAICompatibleRequestPolicy(json => json)], perTryPolicies: ReadOnlySpan<PipelinePolicy>.Empty,
            beforeTransportPolicies: ReadOnlySpan<PipelinePolicy>.Empty);

        PipelineMessage message = pipeline.CreateMessage();
        message.Request.Method = "POST";
        message.Request.Uri = new Uri("http://localhost/v1/chat/completions");
        message.Request.Content = body;
        await pipeline.SendAsync(message);

        Assert.True(body.Disposed);
    }

    /// <summary>改写后中文与 HTML 敏感字符原样：默认编码器会把它们全写成 \uXXXX，请求体凭空大三成</summary>
    [Fact]
    public async Task Rewriter_KeepsNonAsciiUnescaped()
    {
        string rewritten = await Task.Run(() =>
        {
            LlmRequestContext.ForbidToolCalls = true;
            return OpenAICompatibleRequestRewriter.Rewrite(
                "{\"messages\":[{\"role\":\"user\",\"content\":\"你好<>&'+\"}]}", model: null);
        }, TestContext.Current.CancellationToken);

        Assert.Contains("\"content\":\"你好<>&'+\"", rewritten);
    }

    private sealed class ImmediateRetryPolicy() : ClientRetryPolicy(maxRetries: 3)
    {
        protected override TimeSpan GetNextDelay(PipelineMessage message, int tryCount) => TimeSpan.Zero;
    }

    // 前 failures 次回 503，之后回 200，记下每次收到的正文
    private sealed class FlakyServer(int failures, string okBody = "{}") : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content != null) Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(Bodies.Count <= failures && request.Content != null
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK) { Content = new StringContent(okBody, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    // 记下自己有没有被释放的请求体
    private sealed class TrackingContent(string json) : BinaryContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(json);

        public bool Disposed { get; private set; }

        public override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return true;
        }

        public override void WriteTo(Stream stream, CancellationToken cancellation) => stream.Write(_bytes);

        public override Task WriteToAsync(Stream stream, CancellationToken cancellation) =>
            stream.WriteAsync(_bytes, cancellation).AsTask();

        public override void Dispose() => Disposed = true;
    }
}
