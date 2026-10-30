using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 会话亲和绑在<b>客户端实例</b>上，每发请求前落到 <see cref="LlmRequestContext"/> 供 HTTP 层写头。
///
/// 不是「开轮时设一次环境变量」：那样并发会话会互相覆盖，而亲和值一旦串到别的会话上，
/// 那一发就被路由到别人的副本上——比不带更糟。客户端属于某个会话，它发的每一发都该带自己的值。
/// </summary>
public class LazyChatClientSessionAffinityTests
{
    [Fact]
    public async Task Request_CarriesTheClientsAffinity()
    {
        CapturingClient inner = new();
        LazyChatClient client = new(() => Running(inner), "affinity-a");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("affinity-a", inner.SeenAffinity);
    }

    /// <summary>
    /// 两个会话同时发：各自看到各自的亲和值。中途让出线程，逼两条流程真正交错——
    /// 用「开轮时设一次」的写法这里必然串号。
    /// </summary>
    [Fact]
    public async Task ConcurrentClients_KeepTheirOwnAffinity()
    {
        CapturingClient first = new(delayMs: 50);
        CapturingClient second = new(delayMs: 50);
        LazyChatClient firstClient = new(() => Running(first), "affinity-a");
        LazyChatClient secondClient = new(() => Running(second), "affinity-b");

        await Task.WhenAll(
            firstClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
                cancellationToken: TestContext.Current.CancellationToken),
            secondClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("affinity-a", first.SeenAffinity);
        Assert.Equal("affinity-b", second.SeenAffinity);
    }

    /// <summary>没有会话（能力预览那种）时清空：不能沿用同一流程上上一次请求留下的值</summary>
    [Fact]
    public async Task ClientWithoutAffinity_ClearsTheStaleValue()
    {
        // 外层挂着一个值（同一条流上「上一次请求留下的」），而这个客户端没有会话：发出去必须是不带
        using LlmRequestScope.Scope outer =
            LlmRequestScope.Push(new LlmRequestContext { SessionAffinity = "left-over" });
        CapturingClient inner = new();
        LazyChatClient client = new(() => Running(inner));

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(inner.SeenAffinity);
    }

    private static ModelRunningData Running(IChatClient inner)
    {
        RemoteModelInfo remote = new()
        {
            Config = new RemoteAgnesModelConfig
            {
                ModelName = "affinity-test",
                ModelId = "agnes-3.0-flash",
            },
        };
        ModelRunningData data = new(remote);
        data.CompleteLoading(inner, loadToken: CancellationToken.None);
        return data;
    }

    private sealed class CapturingClient(int delayMs = 0) : IChatClient
    {
        public string? SeenAffinity { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (delayMs > 0) await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            SeenAffinity = LlmRequestScope.Current.SessionAffinity;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试只走非流式路径");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
