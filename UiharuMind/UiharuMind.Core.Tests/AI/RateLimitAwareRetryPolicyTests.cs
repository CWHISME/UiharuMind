using UiharuMind.Core.AI.Net;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 钉死退避曲线:限流走 2→32s 的长退避、瞬时故障保持 0.8→3.2s 的短退避，
/// 服务端的 Retry-After 优先但必须封顶——这三条一旦写反，撞限流时要么白等一分钟，
/// 要么继续以 6 秒打完四次的节奏加重拥塞。
/// </summary>
public class RateLimitAwareRetryPolicyTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(5, 32)]
    [InlineData(9, 32)] //到顶后不再翻倍
    public void RateLimited_BacksOffExponentiallyUpTo32s(int tryCount, double expectedSeconds)
    {
        TimeSpan delay = RateLimitAwareRetryPolicy.ComputeDelay(true, tryCount, null);

        Assert.Equal(expectedSeconds, delay.TotalSeconds, 3);
    }

    [Theory]
    [InlineData(1, 0.8)]
    [InlineData(2, 1.6)]
    [InlineData(3, 3.2)]
    [InlineData(8, 3.2)]
    public void Transient_KeepsShortBackoff(int tryCount, double expectedSeconds)
    {
        TimeSpan delay = RateLimitAwareRetryPolicy.ComputeDelay(false, tryCount, null);

        Assert.Equal(expectedSeconds, delay.TotalSeconds, 3);
    }

    /// <summary>
    /// 限流不限次数：撞的是平台侧共享容量，跟自己发多猛无关，等就是唯一的办法；
    /// 放弃的代价是整轮 agent 任务作废。瞬时故障不跟着放开——那是服务端真坏了，
    /// 无限重试只会把明确的失败拖成静默的挂起。
    /// </summary>
    [Fact]
    public void RateLimitRetriesAreUnbounded_TransientOnesStillHaveACeiling()
    {
        Assert.Equal(int.MaxValue, RateLimitAwareRetryPolicy.MaxRetries);
        //瞬时故障仍有个尽头:服务端真坏了的时候,无限重试会把明确的失败拖成静默的挂起
        Assert.True(RateLimitAwareRetryPolicy.TransientMaxRetries < RateLimitAwareRetryPolicy.MaxRetries);
    }

    [Fact]
    public void HugeTryCount_DoesNotOverflowTheBackoff()
    {
        //次数不设上限之后 tryCount 可以涨得很大,指数不夹住会溢出成负数或无穷
        TimeSpan delay = RateLimitAwareRetryPolicy.ComputeDelay(true, int.MaxValue, null);

        Assert.Equal(32, delay.TotalSeconds, 3);
    }

    [Fact]
    public void RetryAfter_WinsOverLocalBackoff()
    {
        TimeSpan delay = RateLimitAwareRetryPolicy.ComputeDelay(true, 1, 7);

        Assert.Equal(7, delay.TotalSeconds, 3);
    }

    [Fact]
    public void RetryAfter_IsCappedAtOneMinute()
    {
        TimeSpan delay = RateLimitAwareRetryPolicy.ComputeDelay(true, 1, 3600);

        Assert.Equal(60, delay.TotalSeconds, 3);
    }

    [Fact]
    public void Jitter_StaysWithinQuarterOfTheDelay()
    {
        TimeSpan baseDelay = TimeSpan.FromSeconds(8);

        for (int i = 0; i < 200; i++)
        {
            double seconds = RateLimitAwareRetryPolicy.ApplyJitter(baseDelay).TotalSeconds;

            Assert.InRange(seconds, 8 * (1 - RateLimitAwareRetryPolicy.JitterRatio),
                8 * (1 + RateLimitAwareRetryPolicy.JitterRatio));
        }
    }

    [Fact]
    public void Jitter_ActuallyVaries()
    {
        TimeSpan baseDelay = TimeSpan.FromSeconds(8);
        HashSet<double> seen = new();

        for (int i = 0; i < 50; i++) seen.Add(RateLimitAwareRetryPolicy.ApplyJitter(baseDelay).TotalSeconds);

        Assert.True(seen.Count > 1, "抖动必须真的随机,否则共享池里所有客户端仍会同步重试");
    }
}
