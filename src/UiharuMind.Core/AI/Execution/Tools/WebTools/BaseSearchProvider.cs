/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Net;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 爬搜索结果页的公共骨架:拟人请求头、发送、解析 DOM、风控识别都在这里,
/// 子类只回答两件事——往哪发、怎么从 DOM 里挑结果。
/// 连接池与 UA 复用 <see cref="WebShared"/>,不再自建 HttpClient。
/// </summary>
internal abstract partial class BaseSearchProvider : ISearchProvider
{
    public abstract string Name { get; }

    /// <summary>子类声明自己请求的基准 Referer，用于 Sec-Fetch 拟人</summary>
    protected abstract Uri DefaultReferer { get; }

    public abstract Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int maxCount, CancellationToken ct);

    /// <summary>
    /// 起一个带拟人头的请求
    /// </summary>
    /// <param name="method">HTTP 方法</param>
    /// <param name="url">目标地址</param>
    /// <returns>请求对象，交给 <see cref="SendAndParseAsync"/> 时由它负责释放</returns>
    protected HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        HttpRequestMessage req = WebShared.CreateFetchRequest(url, method);
        req.Headers.Referrer = DefaultReferer;
        req.Headers.Add("Sec-Fetch-Dest", "document");
        req.Headers.Add("Sec-Fetch-Mode", "navigate");
        req.Headers.Add("Sec-Fetch-Site", "same-origin");
        return req;
    }

    /// <summary>
    /// 从结果页 DOM 里挑条目。子类只需要认得自家的选择器。
    /// </summary>
    /// <param name="doc">结果页 DOM</param>
    /// <param name="maxCount">结果上限</param>
    /// <returns>结果条目</returns>
    protected abstract IEnumerable<SearchResultItem> ParseResults(IDocument doc, int maxCount);

    /// <summary>
    /// 发请求、解析 DOM、挑结果。
    /// </summary>
    /// <param name="request">已备好的请求</param>
    /// <param name="maxCount">结果上限</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>结果列表;命中风控页时为空</returns>
    protected async Task<IReadOnlyList<SearchResultItem>> SendAndParseAsync(
        HttpRequestMessage request, int maxCount, CancellationToken ct)
    {
        using (request)
        {
            using HttpResponseMessage resp = await WebShared.Http.SendAsync(request, ct).ConfigureAwait(false);
            //失败原因交给兜底链去记录并计入熔断,这里不吞
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");

            string html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return await ParseHtmlAsync(html, maxCount).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 把一段结果页 HTML 解析成条目。
    ///
    /// 单独留这个入口是为了能用存档的真实结果页做回归:搜索引擎改版时,选择器会<b>无声</b>失效——
    /// 解析出 0 条,日志只说一句"没搜到",没人会察觉。DDG 就是这么悄悄挂掉的。
    /// BrowsingContext 与 IDocument 都在这里建立并释放:AngleSharp 的 context 不是线程安全的,
    /// 多个 agent 同时搜索时共用一个迟早出事。
    /// </summary>
    /// <param name="html">结果页 HTML</param>
    /// <param name="maxCount">结果上限</param>
    /// <returns>结果列表;命中风控页时为空</returns>
    internal async Task<IReadOnlyList<SearchResultItem>> ParseHtmlAsync(string html, int maxCount)
    {
        using IBrowsingContext context = BrowsingContext.New(Configuration.Default);
        using IDocument doc = await context.OpenAsync(req => req.Content(html)).ConfigureAwait(false);

        return IsBlocked(doc) ? [] : ParseResults(doc, maxCount).ToList();
    }

    /// <summary>
    /// 是否是风控空壳/验证码页。子类有自家特征时覆写，记得并上基类判断。
    /// </summary>
    /// <param name="doc">结果页 DOM</param>
    /// <returns>是风控页返回 true</returns>
    protected virtual bool IsBlocked(IDocument doc)
    {
        return doc.QuerySelector("#cf-challenge, .cf-spinner, form[action*=challenge]") != null
               || doc.Body?.TextContent?.Contains("security check", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// 还原 DDG 的跳转链接（<c>/d?uddg=…</c>）
    /// </summary>
    /// <param name="raw">原始 href</param>
    /// <returns>真实地址；取不到时原样返回</returns>
    protected static string CleanDdgRedirect(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        if (raw.StartsWith("//")) return "https:" + raw;
        if (raw.Contains("/d?", StringComparison.Ordinal))
        {
            Match m = DdgRedirectRegex().Match(raw);
            return m.Success ? WebUtility.UrlDecode(m.Groups["u"].Value) : raw;
        }

        return raw;
    }

    [GeneratedRegex(@"uddg=(?<u>[^&]+)", RegexOptions.Compiled)]
    private static partial Regex DdgRedirectRegex();
}
