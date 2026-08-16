/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using System.Net;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 外部服务的熔断记账,<b>按服务名而非按 provider 实例</b>——Firecrawl 在搜索链和读页链里各有一环,
/// 但共用同一份额度,搜索撞了 429 就没理由让读页再去撞一次。
///
/// 连续失败到阈值就静默一段时间。免 key 额度是按天给的,耗尽后每次调用都要先吃一发 429
/// 再回退,既拖慢每一轮,日志也被刷成一串 miss。
/// </summary>
internal static class WebServiceCircuit
{
    /// <summary>连续失败多少次开始熔断</summary>
    internal const int FailureThreshold = 3;

    /// <summary>熔断持续时长</summary>
    internal static readonly TimeSpan OpenDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 这次失败是不是"服务本身出问题了"。
    ///
    /// 分级是必须的:一个 404 页面、一个 zip 链接只是<b>这个 URL</b>读不了,拿它去熔断,
    /// 三个坏链接就能把整条读取通路停掉五分钟。只有限流、鉴权失败、5xx 和超时才是
    /// 换个 URL 也一样的服务级故障。
    /// </summary>
    /// <param name="e">捕获到的异常</param>
    /// <returns>属于服务级故障返回 true</returns>
    public static bool IsServiceLevelFailure(Exception e)
    {
        return e switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => true,
            HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests } =>
                true,
            HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => true,
            HttpRequestException { StatusCode: null } => true, //连不上,DNS/TLS 之类
            TaskCanceledException => true, //HttpClient 超时
            _ => false
        };
    }

    private sealed class ServiceState
    {
        public int ConsecutiveFailures;
        public long OpenUntilTick;
    }

    private static readonly ConcurrentDictionary<string, ServiceState> States = new();

    /// <summary>
    /// 该服务是否正在熔断
    /// </summary>
    /// <param name="service">服务名(用 provider 的 Name)</param>
    /// <param name="remaining">剩余冷却时长,未熔断时为零</param>
    /// <returns>熔断中返回 true</returns>
    public static bool IsTripped(string service, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (!States.TryGetValue(service, out ServiceState? state)) return false;

        long leftMs = Volatile.Read(ref state.OpenUntilTick) - Environment.TickCount64;
        if (leftMs <= 0) return false;

        remaining = TimeSpan.FromMilliseconds(leftMs);
        return true;
    }

    /// <summary>
    /// 记一次成功,清零连败并立即恢复
    /// </summary>
    /// <param name="service">服务名</param>
    public static void RecordSuccess(string service)
    {
        if (!States.TryGetValue(service, out ServiceState? state)) return;
        Interlocked.Exchange(ref state.ConsecutiveFailures, 0);
        Volatile.Write(ref state.OpenUntilTick, 0);
    }

    /// <summary>
    /// 记一次失败,累计到阈值则熔断
    /// </summary>
    /// <param name="service">服务名</param>
    public static void RecordFailure(string service)
    {
        ServiceState state = States.GetOrAdd(service, _ => new ServiceState());
        if (Interlocked.Increment(ref state.ConsecutiveFailures) < FailureThreshold) return;

        Interlocked.Exchange(ref state.ConsecutiveFailures, 0);
        Volatile.Write(ref state.OpenUntilTick, Environment.TickCount64 + (long)OpenDuration.TotalMilliseconds);
        Log.Warning($"[Web] '{service}' tripped after {FailureThreshold} failures: " +
                    $"skipping it for {OpenDuration.TotalMinutes:F0} min");
    }
}
