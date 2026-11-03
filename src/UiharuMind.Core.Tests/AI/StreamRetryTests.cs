using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net.Errors;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 断线重试的几条口径：故障要认全（SslStream 的裸 <see cref="IOException"/> 与 SDK 包的
/// <see cref="HttpIOException"/> 是同一种断流），重试那一发要带着这一发的请求上下文
/// （异步迭代器里 <c>AsyncLocal</c> 跨 yield 不保留，靠外层 <c>using</c> 挂不住），
/// 退化回复的重发预算不与断线共用。
///
/// <para>
/// 这里钉的是真实现场：日志里 18 次断流，16 次是 <c>HttpIOException</c>、重试一次即恢复；
/// 2 次是裸 <c>IOException</c>、一次都没重试，整轮 agent 直接作废。
/// </para>
/// </summary>
public class StreamRetryTests
{
    /// <summary>
    /// 退化成一片空白（说完一句后吐几百个空白）的重发预算单独计：两次用完就收口，
    /// 而且不走断线的退避——它不是网络问题，等一下不会变好
    /// </summary>
    [Fact]
    public async Task WhitespaceFloodedReply_IsRetriedTwiceThenGivesUp()
    {
        WhitespaceFloodClient fake = new();
        LazyChatClient client = new(() => Running(fake));

        await Assert.ThrowsAnyAsync<DegeneratedReplyException>(async () => await CollectAsync(client));

        // 首发 + 两次重发，第三次仍退化就抛给上层
        Assert.Equal(3, fake.Calls);
    }

    /// <summary>裸 <see cref="IOException"/>（TLS 收到 EOF）与 HttpIOException 一样要重发</summary>
    [Fact]
    public async Task BareTransportEof_IsRetried()
    {
        DroppingClient fake = new(1, new IOException("Received an unexpected EOF or 0 bytes from the transport stream."));
        LazyChatClient client = new(() => Running(fake));

        List<string> texts = await CollectAsync(client);

        Assert.Equal(2, fake.Calls);
        Assert.Equal("ok", texts[^1]);
    }

    /// <summary>
    /// 重试那一发仍带会话亲和与票据回调：亲和丢了等于整段前缀缓存冷启动
    /// （2MB 请求体全量重算），票据回调丢了等于这一发的加密思考白拿
    /// </summary>
    [Fact]
    public async Task Retry_CarriesTheRequestContext()
    {
        DroppingClient fake = new(1, new IOException("boom"));
        LazyChatClient client = new(() => Running(fake), "affinity-x");
        using LlmRequestScope.Scope outer = LlmRequestScope.Push(new LlmRequestContext { ForbidToolCalls = true });

        await CollectAsync(client);

        Assert.Equal(2, fake.Calls);
        // 每条都带：首帧之前、断流那一刻（这一条在修复前必然是 null）、以及第二次尝试
        Assert.All(fake.SeenContexts, seen =>
        {
            Assert.Equal("affinity-x", seen.SessionAffinity);
            Assert.True(seen.ForbidToolCalls); // 外层设的不能被客户端那一层 with 抹掉
            Assert.NotNull(seen.EncryptedReasoningObserved);
        });
    }

    /// <summary>
    /// 发送阶段连接被 reset（<see cref="HttpRequestException"/> 不带状态码）：同样整段重发。
    /// 这一发没花掉任何 token，重发比中途断流更没有代价
    /// </summary>
    [Fact]
    public async Task SendPhaseReset_IsRetried()
    {
        DroppingClient fake = new(1, new HttpRequestException("The response ended prematurely.",
            new IOException("Connection reset by peer")));
        LazyChatClient client = new(() => Running(fake));

        List<string> texts = await CollectAsync(client);

        Assert.Equal(2, fake.Calls);
        Assert.Equal("ok", texts[^1]);
    }

    /// <summary>拨不通（连接被拒）不是断流：退避重发只是白等，直接失败</summary>
    [Fact]
    public async Task UnreachableEndpoint_IsNotRetried()
    {
        IOException error = new("connection refused", new SocketException((int)SocketError.ConnectionRefused));
        DroppingClient fake = new(6, error);
        LazyChatClient client = new(() => Running(fake));

        await Assert.ThrowsAnyAsync<IOException>(async () => await CollectAsync(client));

        Assert.Equal(1, fake.Calls);
    }

    /// <summary>重试不能把客户端的上下文漏给调用方：外层那份用完还得原样回来</summary>
    [Fact]
    public async Task Retry_DoesNotLeakContextToTheCaller()
    {
        DroppingClient fake = new(1, new IOException("boom"));
        LazyChatClient client = new(() => Running(fake), "affinity-inner");
        using LlmRequestScope.Scope outer = LlmRequestScope.Push(new LlmRequestContext { SessionAffinity = "outer" });

        await CollectAsync(client);

        Assert.Equal("outer", LlmRequestScope.Current.SessionAffinity);
    }

    /// <summary>故障判据本身：两种壳都算断流，拨不通与握手失败不算，带状态码的不算</summary>
    [Fact]
    public void TransportFault_ClassifiesTheShellsAndTheUnrecoverableCases()
    {
        Assert.True(TransportFault.IsRetryableDrop(new HttpIOException(HttpRequestError.ResponseEnded)));
        Assert.True(TransportFault.IsRetryableDrop(new IOException("unexpected EOF")));
        Assert.True(TransportFault.IsRetryableDrop(new HttpRequestException("reset", new IOException("reset"))));

        Assert.False(TransportFault.IsRetryableDrop(
            new IOException("refused", new SocketException((int)SocketError.ConnectionRefused))));
        Assert.False(TransportFault.IsRetryableDrop(new HttpRequestException("tls",
            new System.Security.Authentication.AuthenticationException("handshake failed"))));
        // 有状态码说明服务端回过响应：400/500 重发一百次还是同一个结果
        Assert.False(TransportFault.IsRetryableDrop(
            new HttpRequestException("bad request", inner: null, System.Net.HttpStatusCode.BadRequest)));
        Assert.False(TransportFault.IsRetryableDrop(new InvalidOperationException("not a transport fault")));
    }

    /// <summary>每一次尝试都退化成一片空白（说完一句后吐超过阈值的空白），从不成功</summary>
    private sealed class WhitespaceFloodClient : IChatClient
    {
        public int Calls;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("一句话")]);
            yield return new ChatResponseUpdate(ChatRole.Assistant,
                [new TextContent(new string(' ', StreamCompletionWatch.WhitespaceFloodChars + 100))]);
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static async Task<List<string>> CollectAsync(LazyChatClient client)
    {
        List<string> texts = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hi")],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            foreach (AIContent content in update.Contents)
            {
                if (content is TextContent text) texts.Add(text.Text);
            }
        }

        return texts;
    }

    private static ModelRunningData Running(IChatClient inner)
    {
        RemoteModelInfo remote = new()
        {
            Config = new RemoteAgnesModelConfig
            {
                ModelName = "stream-retry-test",
                ModelId = "agnes-3.0-flash",
            },
        };
        ModelRunningData data = new(remote);
        data.CompleteLoading(inner, loadToken: CancellationToken.None);
        return data;
    }

    /// <summary>
    /// 前 <paramref name="failures"/> 次尝试吐半句话之后断流。每次尝试都在「首帧之前」与
    /// 「断流那一刻」各记一次当时的请求上下文——后者跨过了 yield，正是修复前丢掉上下文的位置
    /// </summary>
    private sealed class DroppingClient(int failures, Exception error) : IChatClient
    {
        public int Calls;

        public List<LlmRequestContext> SeenContexts { get; } = [];

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref Calls);
            SeenContexts.Add(LlmRequestScope.Current);
            if (call > failures)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
                yield break;
            }

            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("半截")]);
            await Task.Yield();
            SeenContexts.Add(LlmRequestScope.Current);
            throw error;
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
