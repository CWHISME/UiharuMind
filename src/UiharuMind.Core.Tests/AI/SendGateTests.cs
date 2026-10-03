using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Net;
using UiharuMind.Core.AI.Net;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 共享发送闸（ADR 0058）。时间全由用例推：时钟是个数，等待挂在用例手里，推一步放一位
/// </summary>
public class SendGateTests
{
    private long _now;
    private TaskCompletionSource? _pendingDelay;
    private TimeSpan _pendingWait;

    private SendGate CreateGate() => new("test", () => _now, wait =>
    {
        _pendingWait = wait;
        _pendingDelay = new TaskCompletionSource(); //同步续体：推一步，闸当场放下一位
        return _pendingDelay.Task;
    });

    private void Step()
    {
        TaskCompletionSource delay = Assert.IsType<TaskCompletionSource>(_pendingDelay);
        _pendingDelay = null;
        _now += (long)_pendingWait.TotalMilliseconds;
        delay.SetResult();
    }

    [Fact]
    public void Pacer_StartsOpen()
    {
        SendPacer pacer = new();

        Assert.Equal(TimeSpan.Zero, pacer.Claim(0));
        Assert.Equal(TimeSpan.Zero, pacer.Claim(0));
        Assert.False(pacer.OnSuccess());
    }

    [Fact]
    public void Pacer_RateLimitClosesThenSpacesSends()
    {
        SendPacer pacer = new();
        pacer.Claim(0);

        Assert.True(pacer.OnRateLimited(sentAt: 0, now: 100, retryAfter: null));

        Assert.Equal(SendPacer.StartInterval, pacer.Interval);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), pacer.Claim(100));
        Assert.Equal(TimeSpan.Zero, pacer.Claim(1100));
        Assert.Equal(TimeSpan.FromMilliseconds(1000), pacer.Claim(1100)); //占下一位后，下一位再隔一个间隔
    }

    /// <summary>三条并发在途、一起被打回：只翻一次倍；翻倍之后发出的再撞才接着翻</summary>
    [Fact]
    public void Pacer_ConcurrentRateLimitsWidenOnce()
    {
        SendPacer pacer = new();

        Assert.True(pacer.OnRateLimited(0, 10, null));
        Assert.False(pacer.OnRateLimited(0, 11, null));
        Assert.False(pacer.OnRateLimited(0, 12, null));
        Assert.Equal(TimeSpan.FromSeconds(1), pacer.Interval);

        Assert.True(pacer.OnRateLimited(2000, 2100, null));
        Assert.Equal(TimeSpan.FromSeconds(2), pacer.Interval);
    }

    /// <summary>段 23：一条请求连撞六次，间隔 1→28.8 秒。重试再撞只按当前间隔推后，不再翻倍</summary>
    [Fact]
    public void Pacer_RetryChainWidensOnce()
    {
        SendPacer pacer = new();

        Assert.True(pacer.OnRateLimited(0, 10, null));
        Assert.False(pacer.OnRateLimited(1010, 1020, null, isRetry: true));
        Assert.False(pacer.OnRateLimited(2020, 2030, null, isRetry: true));

        Assert.Equal(SendPacer.StartInterval, pacer.Interval);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), pacer.Claim(2030)); //仍按当前间隔推后
        Assert.True(pacer.OnRateLimited(3100, 3110, null)); //别的请求头一次撞，照常翻倍
        Assert.Equal(TimeSpan.FromSeconds(2), pacer.Interval);
    }

    [Fact]
    public void Pacer_IntervalIsCapped()
    {
        SendPacer pacer = new();
        for (int i = 0; i < 20; i++) pacer.OnRateLimited(i * 100_000, i * 100_000, null);

        Assert.Equal(SendPacer.MaxInterval, pacer.Interval);
    }

    [Fact]
    public void Pacer_RetryAfterPushesNextSendCapped()
    {
        SendPacer pacer = new();

        pacer.OnRateLimited(0, 0, TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(5), pacer.Claim(0));

        pacer.OnRateLimited(0, 0, TimeSpan.FromHours(1));
        Assert.Equal(SendPacer.RetryAfterCap, pacer.Claim(0));
    }

    [Fact]
    public void Pacer_SuccessesShrinkUntilOpen()
    {
        SendPacer pacer = new();
        pacer.OnRateLimited(0, 0, null);

        int successes = 0;
        while (!pacer.OnSuccess()) successes++;

        Assert.Equal(1, successes); //1s → 0.5s → 0.25s，第 2 次撤闸
        Assert.Equal(TimeSpan.Zero, pacer.Interval);
    }

    [Fact]
    public void Gate_OpenPassesWithoutWaiting()
    {
        SendGate gate = CreateGate();

        Assert.True(gate.WaitTurnAsync(false, CancellationToken.None).IsCompletedSuccessfully);
        Assert.True(gate.WaitTurnAsync(false, CancellationToken.None).IsCompletedSuccessfully);
        Assert.Null(_pendingDelay);
    }

    [Fact]
    public void Gate_RetryJumpsTheQueue()
    {
        SendGate gate = CreateGate();
        gate.OnRateLimited(gate.Now, null);

        Task first = gate.WaitTurnAsync(false, CancellationToken.None);
        Task second = gate.WaitTurnAsync(false, CancellationToken.None);
        Task retry = gate.WaitTurnAsync(true, CancellationToken.None);
        Assert.False(first.IsCompleted || second.IsCompleted || retry.IsCompleted);

        Step();
        Assert.True(retry.IsCompleted);
        Assert.False(first.IsCompleted);

        Step();
        Assert.True(first.IsCompleted);
        Assert.False(second.IsCompleted);

        Step();
        Assert.True(second.IsCompleted);
        Assert.Null(_pendingDelay); //队空，闸不再空转
    }

    [Fact]
    public async Task Gate_CancelledWaiterLeavesTheQueue()
    {
        SendGate gate = CreateGate();
        gate.OnRateLimited(gate.Now, null);
        using CancellationTokenSource cancel = new();

        Task leaving = gate.WaitTurnAsync(false, cancel.Token);
        Task staying = gate.WaitTurnAsync(false, CancellationToken.None);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leaving);

        Step();
        Assert.True(staying.IsCompleted); //取消的那位没占掉这个发送位
    }

    /// <summary>真管道：撞一次 429 后重试策略照常退避（约 2 秒），之后仍在闸上排队首；成功后间隔减半</summary>
    [Fact]
    public async Task Pipeline_RateLimitedRetryBacksOffThenQueuesAtTheGate()
    {
        SendGate gate = new("test", () => _now, wait =>
        {
            _now += (long)wait.TotalMilliseconds;
            return Task.CompletedTask;
        });
        RateLimitedOnce server = new();
        ClientPipeline pipeline = ClientPipeline.Create(
            new ClientPipelineOptions
            {
                Transport = new HttpClientPipelineTransport(new HttpClient(server)),
                RetryPolicy = new RateLimitAwareRetryPolicy(),
            },
            perCallPolicies: ReadOnlySpan<PipelinePolicy>.Empty, perTryPolicies: [new SendGatePolicy(gate)],
            beforeTransportPolicies: ReadOnlySpan<PipelinePolicy>.Empty);

        PipelineMessage message = pipeline.CreateMessage();
        message.Request.Method = "POST";
        message.Request.Uri = new Uri("http://localhost/v1/chat/completions");
        Stopwatch elapsed = Stopwatch.StartNew();
        await pipeline.SendAsync(message);

        Assert.Equal(200, message.Response!.Status);
        Assert.Equal(2, server.Requests);
        Assert.Equal(SendPacer.StartInterval.TotalMilliseconds, _now); //在闸上等了一个间隔（模拟时间）
        Assert.Equal(SendPacer.StartInterval * SendPacer.SuccessFactor, gate.Interval);
        Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(1.4), $"retry did not back off: {elapsed.Elapsed}"); //2 秒 ±25% 抖动
    }

    private sealed class RateLimitedOnce : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(++Requests == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)
                { Content = new StringContent("{}") });
    }

    [Fact]
    public void Gate_SameGroupSharesOneGate()
    {
        Assert.Same(SendGate.For("https://a/v1", "k", "m"), SendGate.For("https://a/v1", "k", "m"));
        Assert.NotSame(SendGate.For("https://a/v1", "k", "m"), SendGate.For("https://a/v1", "k2", "m"));
        Assert.NotSame(SendGate.For("https://a/v1", "k", "m"), SendGate.For("https://a/v1", "k", "m2"));
    }
}
