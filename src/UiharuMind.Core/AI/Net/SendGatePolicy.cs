/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ClientModel.Primitives;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 每次尝试发出前在 <see cref="SendGate"/> 上排队，回来后把 429 / 成功报给它。
/// 挂在重试策略之后（<see cref="PipelinePosition.PerTry"/>），重试的每一次都过闸
/// </summary>
internal sealed class SendGatePolicy : PipelinePolicy
{
    private readonly SendGate _gate;

    public SendGatePolicy(SendGate gate)
    {
        _gate = gate;
    }

    /// <summary>
    /// 这条消息是否由闸管着发送节奏：是的话 429 的退避交给闸，重试策略不再另等一遍
    /// </summary>
    /// <param name="message">管道消息</param>
    /// <returns>过过闸为 true</returns>
    public static bool IsGated(PipelineMessage message) => message.TryGetProperty(typeof(Attempt), out _);

    //挂在消息上：有它就说明这是重试
    private sealed class Attempt
    {
        public long SentAt;
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Attempt attempt = Enter(message, out bool isRetry);
        _gate.WaitTurnAsync(isRetry, message.CancellationToken).GetAwaiter().GetResult();
        attempt.SentAt = _gate.Now;
        ProcessNext(message, pipeline, currentIndex);
        Report(message, attempt, isRetry);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        Attempt attempt = Enter(message, out bool isRetry);
        await _gate.WaitTurnAsync(isRetry, message.CancellationToken).ConfigureAwait(false);
        attempt.SentAt = _gate.Now;
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        Report(message, attempt, isRetry);
    }

    private static Attempt Enter(PipelineMessage message, out bool isRetry)
    {
        isRetry = message.TryGetProperty(typeof(Attempt), out object? existing) && existing is Attempt;
        if (isRetry) return (Attempt)existing!;

        Attempt created = new();
        message.SetProperty(typeof(Attempt), created);
        return created;
    }

    private void Report(PipelineMessage message, Attempt attempt, bool isRetry)
    {
        int? status = message.Response?.Status;
        if (status == RateLimitAwareRetryPolicy.RateLimitStatus)
        {
            int? seconds = RateLimitAwareRetryPolicy.ReadRetryAfterSeconds(message);
            _gate.OnRateLimited(attempt.SentAt, seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : null, isRetry);
        }
        else if (status is >= 200 and < 300)
        {
            _gate.OnSuccess();
        }
    }
}
