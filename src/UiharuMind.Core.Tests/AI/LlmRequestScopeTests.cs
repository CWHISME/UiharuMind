using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net.Request;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 每请求上下文的载体：一份不可变快照 + 一个 <c>using</c> 作用域。
///
/// 这几条钉住的是「静态可变状态」退场之后的三条硬语义——
/// 退出即还原（漏复位在类型层面写不出来）、嵌套只叠加不覆盖、并发流程互不可见。
/// 少了任何一条，上一发的思考正文或亲和值就会串到下一发去，
/// 而症状（网关报「历史对不上」、缓存命中率异常）离原因隔着好几层。
/// </summary>
public class LlmRequestScopeTests
{
    /// <summary>没有场景在发请求时是空的那一份，不是 null、也不是「上次的」</summary>
    [Fact]
    public void WithoutScope_CurrentIsEmpty()
    {
        Assert.Same(LlmRequestContext.Empty, LlmRequestScope.Current);
        Assert.False(LlmRequestScope.Current.ForbidToolCalls);
        Assert.Null(LlmRequestScope.Current.SessionAffinity);
        Assert.Null(LlmRequestScope.Current.PendingReasoningSource);
    }

    /// <summary>作用域结束还原成进来时的那份；嵌套时先回到外层，再回到空</summary>
    [Fact]
    public void Scope_RestoresThePreviousContext()
    {
        Assert.Same(LlmRequestContext.Empty, LlmRequestScope.Current);

        using (LlmRequestScope.Push(new LlmRequestContext { SessionAffinity = "outer" }))
        {
            Assert.Equal("outer", LlmRequestScope.Current.SessionAffinity);

            using (LlmRequestScope.Push(new LlmRequestContext { SessionAffinity = "inner" }))
            {
                Assert.Equal("inner", LlmRequestScope.Current.SessionAffinity);
            }

            Assert.Equal("outer", LlmRequestScope.Current.SessionAffinity); //回到外层，不是回到空
        }

        Assert.Same(LlmRequestContext.Empty, LlmRequestScope.Current);
    }

    /// <summary>
    /// 内层只替换自己关心的字段：<c>Current with { … }</c> 的写法下，外层设的禁工具不会被抹掉。
    /// 写交接文档那一发正是靠这个——它在外面禁工具，<see cref="LazyChatClient"/> 在里面挂思考与亲和。
    /// </summary>
    [Fact]
    public void InnerScope_KeepsFieldsItDidNotTouch()
    {
        using (LlmRequestScope.Push(new LlmRequestContext { ForbidToolCalls = true }))
        {
            LlmRequestContext merged = LlmRequestScope.Current with { SessionAffinity = "session" };
            using (LlmRequestScope.Push(merged))
            {
                Assert.True(LlmRequestScope.Current.ForbidToolCalls);
                Assert.Equal("session", LlmRequestScope.Current.SessionAffinity);
            }

            Assert.True(LlmRequestScope.Current.ForbidToolCalls);
            Assert.Null(LlmRequestScope.Current.SessionAffinity);
        }
    }

    /// <summary>
    /// 模型客户端挂上下文时只动自己那两项：外层禁着工具，发出去的那一发仍然是禁的；
    /// 发完之后外层的值还在（客户端的作用域不会把外面设的东西一起清掉）。
    /// </summary>
    [Fact]
    public async Task Client_KeepsTheContextItWasCalledFrom()
    {
        CapturingClient inner = new();
        LazyChatClient client = new(() => Running(inner));
        using LlmRequestScope.Scope outer = LlmRequestScope.Push(new LlmRequestContext
        {
            ForbidToolCalls = true,
            SessionAffinity = "outer-affinity",
        });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(inner.Seen!.ForbidToolCalls);            //外层禁着：发出去就是禁的
        Assert.Null(inner.Seen.SessionAffinity);             //这个客户端没有会话：清掉，不沿用外层
        Assert.NotNull(inner.Seen.PendingReasoningSource);   //思考来源由客户端挂上
        Assert.True(LlmRequestScope.Current.ForbidToolCalls); //客户端退出后外层照旧
        Assert.Equal("outer-affinity", LlmRequestScope.Current.SessionAffinity);
    }

    /// <summary>
    /// 流式路径同样挂得上：作用域写在异步迭代器体内，横跨 yield，
    /// 但内层 SDK 调用发生在迭代器的 MoveNext 里，读得到。
    /// </summary>
    [Fact]
    public async Task StreamingClient_PutsTheContextOnTheWire()
    {
        StreamingCapture inner = new();
        LazyChatClient client = new(() => Running(inner), "stream-affinity");

        await foreach (ChatResponseUpdate _ in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hi")],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal("stream-affinity", inner.Seen!.SessionAffinity);
        Assert.NotNull(inner.Seen.PendingReasoningSource);
    }

    /// <summary>两条并发流程各挂各的：中途真正交错，谁也不该读到对方的</summary>
    [Fact]
    public async Task ConcurrentFlows_KeepTheirOwnContext()
    {
        async Task<string?> Run(string value)
        {
            using LlmRequestScope.Scope scope = LlmRequestScope.Push(new LlmRequestContext { SessionAffinity = value });
            await Task.Delay(20, TestContext.Current.CancellationToken);
            return LlmRequestScope.Current.SessionAffinity;
        }

        string?[] seen = await Task.WhenAll(Run("first"), Run("second"));

        Assert.Equal(["first", "second"], seen);
    }

    /// <summary>
    /// 生产路径（<c>For(model)</c>）读环境，纯函数路径（<c>For(model, context)</c>）只读入参。
    /// 后者是测试的入口：断言「什么情况下要改写」不必先摆一个异步流。
    /// </summary>
    [Fact]
    public void AmbientAndExplicitContext_AreTwoDifferentDoors()
    {
        using LlmRequestScope.Scope scope = LlmRequestScope.Push(new LlmRequestContext { ForbidToolCalls = true });

        Assert.True(RequestRewrite.For(model: null).ForbidToolCalls);
        Assert.False(RequestRewrite.For(null, LlmRequestContext.Empty).ForbidToolCalls);
    }

    private static ModelRunningData Running(IChatClient inner)
    {
        RemoteModelInfo remote = new()
        {
            Config = new RemoteAgnesModelConfig
            {
                ModelName = "scope-test",
                ModelId = "agnes-3.0-flash",
            },
        };
        ModelRunningData data = new(remote);
        data.CompleteLoading(inner, loadToken: CancellationToken.None);
        return data;
    }

    /// <summary>记下发请求那一刻看到的上下文</summary>
    private sealed class CapturingClient : IChatClient
    {
        public LlmRequestContext? Seen { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Seen = LlmRequestScope.Current;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试只走非流式路径");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>流式一侧：读出上下文之后再吐一块，让作用域跨过 yield</summary>
    private sealed class StreamingCapture : IChatClient
    {
        public LlmRequestContext? Seen { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("本测试只走流式路径");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Seen = LlmRequestScope.Current;
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
