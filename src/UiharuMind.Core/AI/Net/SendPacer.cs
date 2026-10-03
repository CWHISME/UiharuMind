/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 一组请求的发送间隔：撞了 429 翻倍、成功缩短、缩到下限就撤闸（间隔归零，见 ADR 0058）。
/// 时间一律是调用方给的毫秒刻度，不碰时钟；不加锁，由 <see cref="SendGate"/> 在自己的锁里调。
/// </summary>
internal sealed class SendPacer
{
    /// <summary>撞上第一次 429 时的起始间隔</summary>
    internal static readonly TimeSpan StartInterval = TimeSpan.FromSeconds(1);

    /// <summary>间隔上限</summary>
    internal static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(30);

    /// <summary>缩到这个以下就撤闸</summary>
    internal static readonly TimeSpan OpenBelow = TimeSpan.FromMilliseconds(500);

    /// <summary>服务端给的等待也要封顶，与重试策略一致</summary>
    internal static readonly TimeSpan RetryAfterCap = TimeSpan.FromSeconds(60);

    /// <summary>每次成功间隔乘上的系数</summary>
    internal const double SuccessFactor = 0.9;

    private long _nextSendAt; //下一次最早可发的刻度
    private long _lastWidenedAt = long.MinValue; //上次翻倍的刻度：那之前发出的请求撞的 429 不再翻倍

    /// <summary>当前间隔；为零即闸已撤，请求不排队</summary>
    public TimeSpan Interval { get; private set; }

    /// <summary>
    /// 能不能现在发；能就占下这个发送位
    /// </summary>
    /// <param name="now">当前刻度（毫秒）</param>
    /// <returns>还要等多久；为零表示已占下、可以发</returns>
    public TimeSpan Claim(long now)
    {
        long wait = _nextSendAt - now;
        if (wait > 0) return TimeSpan.FromMilliseconds(wait);

        _nextSendAt = now + (long)Interval.TotalMilliseconds;
        return TimeSpan.Zero;
    }

    /// <summary>
    /// 记一次 429。并发在途的几条一起被打回只翻倍一次：只认上次翻倍之后发出去的那些
    /// </summary>
    /// <param name="sentAt">这条请求发出时的刻度</param>
    /// <param name="now">当前刻度</param>
    /// <param name="retryAfter">服务端给的等待；没有为 null</param>
    /// <returns>这次翻了倍为 true</returns>
    public bool OnRateLimited(long sentAt, long now, TimeSpan? retryAfter)
    {
        bool widened = sentAt >= _lastWidenedAt;
        if (widened)
        {
            TimeSpan doubled = Interval == TimeSpan.Zero ? StartInterval : Interval * 2;
            Interval = doubled > MaxInterval ? MaxInterval : doubled;
            _lastWidenedAt = now;
        }

        TimeSpan pause = Interval;
        if (retryAfter is { } advised && advised > pause) pause = advised > RetryAfterCap ? RetryAfterCap : advised;
        _nextSendAt = Math.Max(_nextSendAt, now + (long)pause.TotalMilliseconds);
        return widened;
    }

    /// <summary>
    /// 记一次成功：间隔缩短，缩到 <see cref="OpenBelow"/> 以下撤闸
    /// </summary>
    /// <returns>这次撤了闸为 true</returns>
    public bool OnSuccess()
    {
        if (Interval == TimeSpan.Zero) return false;

        Interval *= SuccessFactor;
        if (Interval >= OpenBelow) return false;

        Interval = TimeSpan.Zero;
        return true;
    }
}
