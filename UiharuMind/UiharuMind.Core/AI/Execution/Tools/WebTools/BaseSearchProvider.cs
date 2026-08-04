using System.Net;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using UiharuMind.Core.AI.Execution.Tools.WebTools;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Search;

internal abstract class BaseSearchProvider : ISearchProvider
{
    public abstract string Name { get; }

    /// <summary>
    /// 子类声明自己请求的基准 Referer，用于 Sec-Fetch 拟人
    /// </summary>
    protected abstract Uri DefaultReferer { get; }

    // ---------- 共享 Handler ----------
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        UseCookies = true,
        CookieContainer = new()
    };

    protected static readonly HttpClient Http = new(SharedHandler)
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    protected static readonly IBrowsingContext DomCtx =
        BrowsingContext.New(Configuration.Default);

    // ---------- 公共 Header ----------
    protected HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var req= WebShared.CreateFetchRequest(url,method);
        req.Headers.Referrer = DefaultReferer;
        req.Headers.Add("Sec-Fetch-Dest", "document");
        req.Headers.Add("Sec-Fetch-Mode", "navigate");
        req.Headers.Add("Sec-Fetch-Site", "same-origin");
        return req;
    }

    protected async Task<IDocument?> DoSendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var doc = await DomCtx.OpenAsync(async void (r) =>
        {
            try
            {
                r.Content(await resp.Content.ReadAsStringAsync(ct));
            }
            catch (Exception e)
            {
                Log.Error(e.StackTrace);
            }
        }, ct);
        return doc;
    }

    // ---------- DDG 通用跳转 ----------
    protected static string CleanDdgRedirect(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        if (raw.StartsWith("//")) return "https:" + raw;
        if (raw.Contains("/d?", StringComparison.Ordinal))
        {
            var m = Regex.Match(raw, @"uddg=(?<u>[^&]+)");
            return m.Success ? WebUtility.UrlDecode(m.Groups["u"].Value) : raw;
        }

        return raw;
    }

    // ---------- 验证码空壳 ----------
    protected static bool IsBlockedPage(AngleSharp.Dom.IDocument doc)
    {
        // DDG 风控 / Bing 验证码特征
        return doc.QuerySelector("#cf-challenge, .cf-spinner, form[action*=challenge]") != null
               || doc.Body?.TextContent?.Contains("security check", StringComparison.OrdinalIgnoreCase) == true;
    }

    public abstract Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int maxCount, CancellationToken ct);
}