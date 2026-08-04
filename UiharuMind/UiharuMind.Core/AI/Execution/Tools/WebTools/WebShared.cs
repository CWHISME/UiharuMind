// Search/SearchShared.cs

using System.Net;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

internal static class WebShared
{
    private static readonly SocketsHttpHandler Handler = new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        UseCookies = true,
        CookieContainer = new()
    };

    public static readonly HttpClient Http = new(Handler)
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <summary>
    /// 所有对搜索引擎/目标网页的请求，统一从这里起手
    /// </summary>
    public static HttpRequestMessage CreateFetchRequest(string url, HttpMethod? method = null)
    {
        var req = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        req.Headers.Clear();
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36 Edg/150.0.0.0");
        req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        req.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6");
        req.Headers.Add("DNT", "1");
        req.Headers.Add("Upgrade-Insecure-Requests", "1");

        return req;
    }
}