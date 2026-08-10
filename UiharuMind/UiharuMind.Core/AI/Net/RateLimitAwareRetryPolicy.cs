/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ClientModel.Primitives;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 远程模型的重试策略：把限流(429)和瞬时故障(5xx/网络)分开对待。
///
/// SDK 默认是 3 次重试、0.8/1.6/3.2s，对限流太急——免费档模型的 TPM 是平台侧共享容量，
/// 拥塞窗口远不止 6 秒，四次尝试挤在里面只会既失败又加重拥塞。限流因此改成 5 次、2→32s，
/// 并加 ±25% 抖动：共享池里所有客户端同步重试比退避长度本身更致命。
///
/// 瞬时故障仍走短退避——502 多半几百毫秒就恢复，让它等一分钟纯属浪费。
/// </summary>
internal sealed class RateLimitAwareRetryPolicy : ClientRetryPolicy
{
    /// <summary>限流的重试次数上限</summary>
    internal const int MaxRetries = 5;

    /// <summary>瞬时故障的重试次数上限（沿用 SDK 默认口径）</summary>
    internal const int TransientMaxRetries = 3;

    /// <summary>抖动幅度（相对退避时长的比例）</summary>
    internal const double JitterRatio = 0.25;

    private const int RateLimitStatus = 429;
    private static readonly TimeSpan RateLimitBaseDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RateLimitMaxDelay = TimeSpan.FromSeconds(32);
    private static readonly TimeSpan TransientBaseDelay = TimeSpan.FromSeconds(0.8);
    private static readonly TimeSpan TransientMaxDelay = TimeSpan.FromSeconds(3.2);
    private static readonly TimeSpan RetryAfterCap = TimeSpan.FromSeconds(60); //服务端给的等待也要封顶,否则一个离谱的值就能把界面挂住

    public RateLimitAwareRetryPolicy() : base(MaxRetries)
    {
    }

    //按消息计数:策略实例被所有并发请求共用,计数只能挂在消息自己的属性包上
    private sealed class AttemptCounter
    {
        public int Value;
    }

    protected override void OnSendingRequest(PipelineMessage message)
    {
        CounterOf(message).Value++;
        base.OnSendingRequest(message);
    }

    protected override bool ShouldRetry(PipelineMessage message, Exception? exception)
    {
        return Decide(message, base.ShouldRetry(message, exception));
    }

    protected override async ValueTask<bool> ShouldRetryAsync(PipelineMessage message, Exception? exception)
    {
        return Decide(message, await base.ShouldRetryAsync(message, exception).ConfigureAwait(false));
    }

    protected override TimeSpan GetNextDelay(PipelineMessage message, int tryCount)
    {
        bool rateLimited = IsRateLimited(message);
        TimeSpan delay = ApplyJitter(ComputeDelay(rateLimited, tryCount, ReadRetryAfterSeconds(message)));
        Log.Warning($"Remote model {(rateLimited ? "rate limited (429)" : "request failed")}, " +
                    $"retry #{tryCount} in {delay.TotalSeconds:0.#}s.");
        return delay;
    }

    /// <summary>
    /// 在 SDK 的可重试判定之上加两条：本轮已取消就不再重试；
    /// 非限流的失败按 <see cref="TransientMaxRetries"/> 提前收手（限流才用满 <see cref="MaxRetries"/>）。
    /// </summary>
    /// <param name="message">管道消息</param>
    /// <param name="baseDecision">SDK 默认判定</param>
    /// <returns>是否重试</returns>
    private bool Decide(PipelineMessage message, bool baseDecision)
    {
        if (message.CancellationToken.IsCancellationRequested) return false;
        if (!baseDecision) return false;
        if (IsRateLimited(message)) return true;

        return CounterOf(message).Value <= TransientMaxRetries;
    }

    private static bool IsRateLimited(PipelineMessage message)
    {
        return message.Response?.Status == RateLimitStatus;
    }

    private static AttemptCounter CounterOf(PipelineMessage message)
    {
        if (message.TryGetProperty(typeof(AttemptCounter), out object? existing) &&
            existing is AttemptCounter counter)
        {
            return counter;
        }

        AttemptCounter created = new();
        message.SetProperty(typeof(AttemptCounter), created);
        return created;
    }

    /// <summary>
    /// 读取服务端给的 <c>Retry-After</c>（秒）。只认整秒形式——HTTP-date 形式在兼容服务里没见过，
    /// 为它引入时钟依赖不值当；解析不出来就返回 null 走本地退避。
    /// </summary>
    /// <param name="message">管道消息</param>
    /// <returns>秒数；无或不可解析时为 null</returns>
    private static int? ReadRetryAfterSeconds(PipelineMessage message)
    {
        if (message.Response?.Headers.TryGetValue("Retry-After", out string? value) != true) return null;
        return int.TryParse(value, out int seconds) && seconds > 0 ? seconds : null;
    }

    /// <summary>
    /// 退避时长（不含抖动）。服务端给了 <c>Retry-After</c> 就听它的（封顶），否则按指数退避。
    /// </summary>
    /// <param name="rateLimited">是否为限流</param>
    /// <param name="tryCount">第几次重试，从 1 开始</param>
    /// <param name="retryAfterSeconds">服务端给的等待秒数，可空</param>
    /// <returns>退避时长</returns>
    internal static TimeSpan ComputeDelay(bool rateLimited, int tryCount, int? retryAfterSeconds)
    {
        if (retryAfterSeconds is > 0)
        {
            TimeSpan advised = TimeSpan.FromSeconds(retryAfterSeconds.Value);
            return advised > RetryAfterCap ? RetryAfterCap : advised;
        }

        TimeSpan baseDelay = rateLimited ? RateLimitBaseDelay : TransientBaseDelay;
        TimeSpan maxDelay = rateLimited ? RateLimitMaxDelay : TransientMaxDelay;
        int steps = Math.Max(0, tryCount - 1);
        double scaled = baseDelay.TotalSeconds * Math.Pow(2, Math.Min(steps, 16)); //指数先夹住再算,避免大 tryCount 溢出
        return scaled >= maxDelay.TotalSeconds ? maxDelay : TimeSpan.FromSeconds(scaled);
    }

    /// <summary>
    /// 施加 ±<see cref="JitterRatio"/> 的随机抖动。共享配额池里所有客户端同步重试会把拥塞窗口
    /// 拉长，这一条比退避时长本身更要紧。
    /// </summary>
    /// <param name="delay">退避时长</param>
    /// <returns>抖动后的时长</returns>
    internal static TimeSpan ApplyJitter(TimeSpan delay)
    {
        double factor = 1 + (Random.Shared.NextDouble() * 2 - 1) * JitterRatio;
        return TimeSpan.FromSeconds(delay.TotalSeconds * factor);
    }
}
