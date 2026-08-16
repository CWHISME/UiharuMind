/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 正文读取兜底链,与 <see cref="FallbackSearchEngine"/> 同构:
/// 无 key 即可用、能吃 JS 渲染页与 PDF 的 Firecrawl 优先,自己直连的读取器殿后。
/// 抽取出的正文过短同样算失败——多半是拿到了空壳,继续下一环比交给模型一段噪音强。
/// 结果走 <see cref="PageContentCache"/>,重复读同一个 URL 不会重复抓。
/// </summary>
internal sealed class FallbackPageReader
{
    private const int MinContentLength = 150;

    private readonly IPageReader[] _chain =
    {
        new FirecrawlPageReader(),
        new DirectPageReader()
    };

    private readonly PageContentCache _cache = new();

    /// <summary>
    /// 依次尝试各读取器,返回第一份像样的正文
    /// </summary>
    /// <param name="url">目标地址</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>正文;全链走空时返回汇总了各环失败原因的 [Error] 文本</returns>
    public Task<string> ReadAsync(string url, CancellationToken ct)
    {
        return _cache.GetOrFetchAsync(url, token => ReadFreshAsync(url, token), ct);
    }

    private async Task<PageContentCache.PageFetchOutcome> ReadFreshAsync(string url, CancellationToken ct)
    {
        StringBuilder errors = new();

        foreach (IPageReader reader in _chain)
        {
            if (!reader.CanRead(url))
            {
                Log.Debug($"[WebFetch] skip '{reader.Name}': not applicable to {url}");
                continue;
            }

            if (WebServiceCircuit.IsTripped(reader.Name, out TimeSpan cooldown))
            {
                Log.Debug($"[WebFetch] skip '{reader.Name}': circuit open, {cooldown.TotalSeconds:F0}s left");
                continue;
            }

            PageReadResult result;
            try
            {
                result = await reader.ReadAsync(url, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; //调用方主动取消才中断;HttpClient 自身超时同样是 OCE,那属于本环失败
            }
            catch (Exception e)
            {
                //只有服务级故障才计入熔断:单个 URL 读不了不该连累后面所有 URL
                if (WebServiceCircuit.IsServiceLevelFailure(e)) WebServiceCircuit.RecordFailure(reader.Name);
                result = PageReadResult.Fail(e.Message);
            }

            if (result.Content is { } text && text.Length >= (result.IsExact ? 1 : MinContentLength))
            {
                WebServiceCircuit.RecordSuccess(reader.Name);
                Log.Debug($"[WebFetch] hit '{reader.Name}': {text.Length} chars from {url}");
                return new PageContentCache.PageFetchOutcome(text, Cacheable: true);
            }

            string reason = result.Error ?? $"no readable text ({result.Content?.Length ?? 0} chars)";
            Log.Warning($"[WebFetch] miss '{reader.Name}' on {url}: {reason}");
            if (errors.Length > 0) errors.Append("; ");
            errors.Append($"{reader.Name}: {reason}");
        }

        Log.Warning($"[WebFetch] all readers failed on {url}");
        string message = errors.Length > 0
            ? $"[Error] Could not read this page ({errors})."
            : "[Error] No page reader was able to handle this URL.";
        return new PageContentCache.PageFetchOutcome(message, Cacheable: false);
    }
}
