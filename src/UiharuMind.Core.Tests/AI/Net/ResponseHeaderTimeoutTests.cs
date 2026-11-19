using System.ClientModel.Primitives;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.AI.Net.Errors;
using UiharuMind.Core.AI.Net.Transport;

namespace UiharuMind.Core.Tests.AI.Net;

/// <summary>
/// 等响应头看门狗（P0）：响应头迟迟不来时按断流抛 <see cref="ResponseHeaderTimeoutException"/>，
/// SDK 层<b>不</b>在内部无限重试它（要交给上层 LazyChatClient 的带预算断流重发链），
/// 且 SDK 层每重试一次都记进 <see cref="LlmRequestContext.RetryTally"/> 让账本可见。
/// </summary>
public class ResponseHeaderTimeoutTests
{
    private static readonly TimeSpan TinyTimeout = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task HeaderTimeout_WireHandlerThrowsRetryableDrop()
    {
        WireHttpHandler handler = new("http://localhost/v1/chat/completions",
            new HangingHandler(), headerTimeout: TinyTimeout);
        using HttpClient http = new(handler) { Timeout = Timeout.InfiniteTimeSpan };

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            http.SendAsync(new HttpRequestMessage(HttpMethod.Post, "http://localhost/v1/chat/completions"),
                TestContext.Current.CancellationToken));

        ResponseHeaderTimeoutException? leaf = ExceptionChain.Find<ResponseHeaderTimeoutException>(thrown);
        Assert.NotNull(leaf);
        Assert.True(TransportFault.IsRetryableDrop(thrown), "响应头超时必须按断流认，才进得了既有重发链");
    }

    [Fact]
    public void HeaderTimeout_TransportFaultRecognizesIt()
    {
        Assert.True(TransportFault.IsRetryableDrop(new ResponseHeaderTimeoutException(TinyTimeout)));
    }

    /// <summary>
    /// SDK 多次尝试都抛异常时把各次异常包成 AggregateException（ClientRetryPolicy）；
    /// 不穿它，「首发连接重置 + 次发等响应头超时」这类混合失败会在 StreamOnceAsync 的
    /// catch 过滤器处落到 Fatal，整轮作废。保守语义：有一项不可恢复（TLS/拨不通）就不重发。
    /// </summary>
    [Fact]
    public void AggregateException_AllInnerMustBeRetryable()
    {
        Assert.True(TransportFault.IsRetryableDrop(new AggregateException(
            new HttpRequestException("connection reset by peer"),
            new ResponseHeaderTimeoutException(TinyTimeout))));

        Assert.True(TransportFault.IsRetryableDrop(new AggregateException(
            new ResponseHeaderTimeoutException(TinyTimeout),
            new ResponseHeaderTimeoutException(TinyTimeout))));

        Assert.False(TransportFault.IsRetryableDrop(new AggregateException(
            new IOException("drop"),
            new System.Security.Authentication.AuthenticationException("tls"))));
    }

    /// <summary>等响应头超时不断流重发的出口：SDK 层不吞它，交给上层（每次尝试都可见、有预算）</summary>
    [Fact]
    public async Task HeaderTimeout_SdkDoesNotRetryIt()
    {
        int attempts = 0;
        ResponseHeaderTimeoutException? failure = null;

        HangingHandler server = new(onCancelled: () => Interlocked.Increment(ref attempts));
        WireHttpHandler wire = new("http://localhost/v1/chat/completions", server, headerTimeout: TinyTimeout);
        ClientPipeline pipeline = ClientPipeline.Create(new ClientPipelineOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(wire) { Timeout = Timeout.InfiniteTimeSpan }),
            RetryPolicy = new RateLimitAwareRetryPolicy(),
        });

        PipelineMessage message = pipeline.CreateMessage();
        message.Request.Method = "POST";
        message.Request.Uri = new Uri("http://localhost/v1/chat/completions");
        try
        {
            //SDK 重试一旦重启超时就是真挂死；用 WaitAsync 兜底，回归时给出清晰失败而不是拖到 xunit 超时
            await pipeline.SendAsync(message).AsTask().WaitAsync(TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        }
        catch (Exception e) when (ExceptionChain.Find<ResponseHeaderTimeoutException>(e) is { } header)
        {
            failure = header;
        }

        Assert.NotNull(failure);
        Assert.Contains("response headers", failure.Message);
        Assert.Null(message.Response); //响应头从未到达
        Assert.Equal(1, attempts); //SDK 重试策略没有在内部把它再打一轮：一次尝试即上抛
    }

    /// <summary>429 在 SDK 层每重试一次都记进 RetryTally，账本才看得到这次调用试了几次</summary>
    [Fact]
    public async Task RateLimited_SdkRetriesAreTallied()
    {
        LlmRetryTally tally = new();
        using LlmRequestScope.Scope scope = LlmRequestScope.Push(LlmRequestContext.Empty with { RetryTally = tally });
        RateLimitedOnce server = new();
        WireHttpHandler wire = new("http://localhost/v1/chat/completions", server);
        ClientPipeline pipeline = ClientPipeline.Create(new ClientPipelineOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(wire) { Timeout = Timeout.InfiniteTimeSpan }),
            RetryPolicy = new RateLimitAwareRetryPolicy(),
        });

        PipelineMessage message = pipeline.CreateMessage();
        message.Request.Method = "POST";
        message.Request.Uri = new Uri("http://localhost/v1/chat/completions");
        await pipeline.SendAsync(message);

        Assert.Equal(200, message.Response!.Status);
        Assert.Equal(2, server.Requests); //首发 429 + 1 次重试成功
        Assert.Equal(1, tally.Retries); //那 1 次 SDK 内部重试已记账
    }

    /// <summary>挂起的内层网络：永远不回头（模拟连接被黑洞 / 上传被网关丢弃）</summary>
    private sealed class HangingHandler(Action? onCancelled = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                onCancelled?.Invoke();
                throw;
            }

            throw new System.Diagnostics.UnreachableException();
        }
    }

    /// <summary>第一发 429，第二发 200（与 SendGateTests 同款）</summary>
    private sealed class RateLimitedOnce : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            int attempt = Interlocked.Increment(ref Requests);
            HttpResponseMessage response = new(attempt == 1 ? System.Net.HttpStatusCode.TooManyRequests
                : System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") };
            return Task.FromResult(response);
        }
    }
}