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
/// 无 key 即可用、能吃 JS 渲染页与 PDF 的 Firecrawl 优先,自己扒 DOM 的直连殿后。
/// 读出的正文过短同样算失败——多半是拿到了空壳,继续下一环比交给模型一段噪音强。
/// </summary>
internal sealed class FallbackPageReader
{
    private const int MinContentLength = 150;

    private readonly IPageReader[] _chain =
    {
        new FirecrawlPageReader(),
        new HtmlPageReader()
    };

    /// <summary>
    /// 依次尝试各读取器,返回第一份像样的正文
    /// </summary>
    /// <param name="url">目标地址</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>正文;全链走空时返回汇总了各环失败原因的 [Error] 文本</returns>
    public async Task<string> ReadAsync(string url, CancellationToken ct)
    {
        StringBuilder errors = new();

        foreach (IPageReader reader in _chain)
        {
            if (!reader.CanRead(url))
            {
                Log.Debug($"[WebFetch] skip '{reader.Name}': not applicable to {url}");
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
                result = PageReadResult.Fail(e.Message);
            }

            if (result.Content is { Length: >= MinContentLength } text)
            {
                Log.Debug($"[WebFetch] hit '{reader.Name}': {text.Length} chars from {url}");
                return text;
            }

            string reason = result.Error ?? $"no readable text ({result.Content?.Length ?? 0} chars)";
            Log.Warning($"[WebFetch] miss '{reader.Name}' on {url}: {reason}");
            if (errors.Length > 0) errors.Append("; ");
            errors.Append($"{reader.Name}: {reason}");
        }

        Log.Warning($"[WebFetch] all readers failed on {url}");
        return $"[Error] Could not read this page ({errors}).";
    }
}
