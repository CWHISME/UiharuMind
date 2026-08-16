// Search/SearchShared.cs

using System.Net;
using System.Net.Sockets;
using System.Text.Json;

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
        req.Headers.Add("Sec-CH-UA-Mobile", "?0");
        req.Headers.Add("Sec-CH-UA-Platform", "\"macOS\"");
        req.Headers.Add("Sec-Fetch-User", "?1");

        return req;
    }

    /// <summary>本地/内网常见后缀，命中即视作内网</summary>
    private static readonly string[] PrivateHostSuffixes =
        [".local", ".localdomain", ".internal", ".intranet", ".lan", ".home.arpa"];

    /// <summary>
    /// 判断地址是否指向本机或内网。命中的地址不能交给 Firecrawl 这类第三方服务——
    /// 它在自己的机器上解析，既读不到你的内网，还把地址泄露出去了。
    /// </summary>
    /// <param name="url">目标地址</param>
    /// <returns>指向本机或内网返回 true；解析不了则返回 false，交由下游按普通请求报错</returns>
    public static bool IsLocalOrPrivateHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
        if (uri.IsLoopback) return true; //localhost / 127.x / ::1

        string host = uri.Host.Trim('[', ']'); //IPv6 的 Host 带方括号
        if (IPAddress.TryParse(host, out IPAddress? ip)) return IsPrivateAddress(ip);

        // 单标签主机名(nas、gitlab 之类)只可能是内网机器；其余看后缀
        return !host.Contains('.') ||
               PrivateHostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPrivateAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        byte[] bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 //10/8
                   || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) //172.16/12
                   || (bytes[0] == 192 && bytes[1] == 168) //192.168/16
                   || (bytes[0] == 169 && bytes[1] == 254) //169.254/16 链路本地
                   || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127); //100.64/10 CGNAT,Tailscale 之类
        }

        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (bytes[0] & 0xFE) == 0xFC; //fc00::/7 ULA
    }

    /// <summary>
    /// 取 JSON 里的字符串字段，缺失或类型不符都返回空串——各家搜索 API 的响应形状都不保证
    /// </summary>
    /// <param name="element">所在对象</param>
    /// <param name="name">字段名</param>
    /// <returns>字段值，取不到时为空串</returns>
    public static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }
}