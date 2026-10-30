/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Net;
using System.Net.Http.Headers;

namespace UiharuMind.Core.AI.Net.Transport;

/// <summary>
/// 在 <see cref="SendGate"/> 上排队的 HTTP 层版本。
///
/// <para><b>为什么要有这一份</b>：闸的逻辑（间隔、排队、429 后拉长间隔）是协议无关的，
/// 但挂在它上面的 <see cref="SendGatePolicy"/> 是 System.ClientModel 的管道策略——
/// Anthropic 线走官方 SDK 的裸 <see cref="HttpClient"/>，那条管道进不去，
/// 于是这条线上的请求会完全不受闸约束：群里几名成员挂同一把密钥并行时一起撞限流。
/// 闸本身照旧（间隔、排队、429 记账都在 <see cref="SendGate"/> 里），只是换了个挂法。
///
/// <para><b>与管道版的一处差别</b>：管道版能认出「这次是重试」并把它排到队首
/// （<see cref="SendGatePolicy"/> 用消息属性标记），这一层认不出——SDK 的重试是<b>重新发一次
/// HTTP 请求</b>，从这里看就是一次全新的发送，只能排到队尾。差别只影响「重试插队」，
/// 不影响闸的核心（别一起撞上）。
/// </summary>
internal sealed class SendGateHandler : DelegatingHandler
{
    private readonly SendGate _gate;

    /// <summary>
    /// 指定内层处理器
    /// </summary>
    /// <param name="gate">这一组共用的闸</param>
    /// <param name="inner">内层处理器</param>
    public SendGateHandler(SendGate gate, HttpMessageHandler inner) : base(inner)
    {
        _gate = gate;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitTurnAsync(isRetry: false, cancellationToken).ConfigureAwait(false);
        long sentAt = _gate.Now;

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == (HttpStatusCode)429)
        {
            _gate.OnRateLimited(sentAt, ReadRetryAfter(response), isRetry: false);
        }
        else if (response.IsSuccessStatusCode)
        {
            _gate.OnSuccess();
        }

        return response;
    }

    /// <summary>读服务端给的等待时长；没有或认不出来时为 null</summary>
    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta) return delta;
        if (retryAfter?.Date is { } date) return date - DateTimeOffset.UtcNow;
        return null;
    }
}
