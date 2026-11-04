/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Extensions;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 浏览器调用的审批分级（ADR 0079），是浏览器版的「越界写入贯穿三档」。三类：
/// <list type="bullet">
/// <item><b>恒问</b>（<see cref="RequiresApprovalInAllModes"/>）：接管、上传、内网段设备与非 http(s) 地址。
/// 接管等的是用户的手；上传送出去收不回；内网段够得着别人的设备（路由器后台、NAS、云元数据）。
/// 只看地址字面、不解析 DNS：解析到内网的公网域名拦不住，与越界写入不解析软链同一个取舍。</item>
/// <item><b>只认「扩展不再询问」</b>（<see cref="RequiresSessionWideApproval"/>）：本机起的服务（localhost、回环、
/// <c>*.local</c>）与工作区之外的本地文件。读遍本机是用户该自己拍板的事，所以要让用户明确勾一次，
/// 点单个扩展的「本会话允许」不算。</item>
/// <item><b>一般级</b>：公网页面，以及落在本会话界内的 <c>file://</c> 页面（工作区/草稿房间里我们自己生成的
/// 拾取器、报告，与「界内写入」同一口径）。</item>
/// </list>
/// 往密码框打字不在这里：它不问，工具当场拒（见 <c>PuppeteerTab.Actions</c>），点过「本会话允许」也拦得住。
/// </summary>
internal static class BrowserRisk
{
    /// <summary>导航目标的性质；审批三档按它分</summary>
    internal enum ENavigationTarget
    {
        /// <summary>公网 http(s)：一般级</summary>
        Public,

        /// <summary>about:blank 之类，什么也够不着</summary>
        About,

        /// <summary>本会话界内的本地页面：一般级</summary>
        InScopeFile,

        /// <summary>工作区之外的本地文件：只认「扩展不再询问」那一格</summary>
        LocalFile,

        /// <summary>本机起的服务（localhost、回环、*.local）：同上</summary>
        LocalService,

        /// <summary>内网段与链路本地（别人的设备、云元数据）：恒问</summary>
        PrivateNetwork,

        /// <summary>其它非 http(s) 协议（data:、chrome: 等）：恒问</summary>
        OtherScheme,
    }

    /// <summary>
    /// 这次调用是否任何档位、任何放行都要审批
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <returns>要审批为 true；不是浏览器调用、或导航没给地址（参数绑定当场失败，什么也打不开）为 false</returns>
    public static bool RequiresApprovalInAllModes(FunctionCallContent call) => call.Name switch
    {
        BrowserTools.HandoffToolName => true,
        BrowserTools.NavigateToolName => ExtractText(call.Arguments, "url") is { Length: > 0 } url &&
                                         TargetOf(url) is ENavigationTarget.PrivateNetwork or ENavigationTarget.OtherScheme,
        BrowserTools.ActToolName => string.Equals(ExtractText(call.Arguments, "action")?.Trim(), "upload",
            StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>
    /// 这次调用是不是「只有勾了本会话扩展不再询问才放行」的那一类：本机服务与工作区之外的本地文件
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <param name="context">判风险要用的外部事实：界内路径判据用来区分界内页面与界外文件</param>
    /// <returns>是这一类为 true</returns>
    public static bool RequiresSessionWideApproval(FunctionCallContent call, ExtensionRiskContext? context = null) =>
        call.Name == BrowserTools.NavigateToolName &&
        ExtractText(call.Arguments, "url") is { Length: > 0 } url &&
        TargetOf(url, context) is ENavigationTarget.LocalFile or ENavigationTarget.LocalService;

    /// <summary>
    /// 地址是否指向本机、内网或非 http(s)
    /// </summary>
    /// <param name="url">地址；没写协议的按 https 理解（与导航工具一致）</param>
    /// <returns>是为 true</returns>
    internal static bool IsLocalTarget(string url) =>
        TargetOf(url) is not (ENavigationTarget.Public or ENavigationTarget.About);

    /// <summary>地址的性质</summary>
    /// <param name="url">地址</param>
    /// <param name="context">判风险要用的外部事实（判 file:// 在不在界内）</param>
    /// <returns>性质</returns>
    internal static ENavigationTarget TargetOf(string url, ExtensionRiskContext? context = null)
    {
        // 建不出来：浏览器那边也打不开（Chrome 会剥的字符上面已经剥过）。按非 http(s) 处理 = 恒问：
        // 判成「公网」是 fail-open——判错了就直接被单扩展放行
        if (!Uri.TryCreate(ToAbsoluteUrl(url), UriKind.Absolute, out Uri? uri)) return ENavigationTarget.OtherScheme;
        if (uri.Scheme == "about") return ENavigationTarget.About;
        if (uri.IsFile) return IsInScopeFile(uri, context) ? ENavigationTarget.InScopeFile : ENavigationTarget.LocalFile;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return ENavigationTarget.OtherScheme;
        if (uri.IsLoopback) return ENavigationTarget.LocalService;

        string host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return ENavigationTarget.LocalService;
        }

        return IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? address) && IsPrivate(address)
            ? ENavigationTarget.PrivateNetwork
            : ENavigationTarget.Public;
    }

    /// <summary>
    /// 判地址前先抹平浏览器自己会抹平的东西：Tab、回车、换行（Chrome 直接剥掉）与反斜杠（当 <c>/</c> 用）。
    /// 不抹的话 .NET 建不出 Uri，而 Chrome 照样打开——判定会落到「公网」那一侧
    /// </summary>
    /// <param name="url">模型给的地址</param>
    /// <returns>抹平后的地址</returns>
    internal static string Normalize(string url)
    {
        StringBuilder builder = new(url.Length);
        foreach (char c in url)
        {
            if (c is '\t' or '\r' or '\n') continue;
            builder.Append(c == '\\' ? '/' : c);
        }

        return builder.ToString().Trim();
    }

    // 已经在地址里写了的协议。file: 的两种斜杠写法都算：file:/C:/x.html 在 Chrome 里就是本地文件
    private static readonly string[] KnownSchemes =
    [
        "file:", "about:", "data:", "blob:", "view-source:", "javascript:", "chrome:", "chrome-extension:",
    ];

    /// <summary>
    /// 补齐协议：模型常写 <c>example.com</c> 不带协议。
    /// <b>判定与执行共用这一份</b>——两边各写一份就会出现「审批按公网判、浏览器却当文件打开」
    /// </summary>
    /// <param name="url">模型给的地址</param>
    /// <returns>可以交给浏览器与 Uri 的地址</returns>
    internal static string ToAbsoluteUrl(string url)
    {
        string normalized = Normalize(url);
        if (normalized.Length == 0) return normalized;
        // C:/x.html 在 Chrome 地址栏里就是本地文件；不认出来会被补成 https://C:/x.html
        if (LooksLikeDrivePath(normalized)) return "file:///" + normalized;
        // 协议相对写法 //host/path：Chrome 按当前页协议打开，没有当前页时按 https
        if (normalized.StartsWith("//", StringComparison.Ordinal)) return "https:" + normalized;
        // http(s) 的少斜杠（http:/host）与多余斜杠（https:///host）都折成标准形：
        // .NET 的 Uri 建不出来，而 Chrome 认得
        if (normalized.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            int schemeLength = normalized.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ? 6 : 5;
            return normalized[..schemeLength] + "//" + normalized[schemeLength..].TrimStart('/');
        }

        if (normalized.Contains("://", StringComparison.Ordinal)) return normalized;
        return KnownSchemes.Any(scheme => normalized.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            ? normalized
            : "https://" + normalized;
    }

    private static bool LooksLikeDrivePath(string url) =>
        url.Length >= 3 && char.IsAsciiLetter(url[0]) && url[1] == ':' && url[2] == '/';

    /// <summary>这个 file:// 是不是本会话界内的页面；没有界内判据（旧调用点、测试）一律按界外。
    /// 判据是字面比（不解析软链）：工作区里一条指向界外的软链能绕过它——与写入侧同一取舍，
    /// 真正的边界是用户勾「不再询问」的那一下</summary>
    private static bool IsInScopeFile(Uri uri, ExtensionRiskContext? context)
    {
        if (context?.IsPathInScope == null) return false;
        string path;
        try
        {
            path = uri.LocalPath;
        }
        catch (InvalidOperationException)
        {
            return false; // 地址能建出来但取不出本地路径：按界外
        }

        return path.Length > 0 && context.IsPathInScope(path);
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            0 or 10 or 127 => true,
            100 => bytes[1] is >= 64 and <= 127, //运营商级 NAT
            169 => bytes[1] == 254,
            172 => bytes[1] is >= 16 and <= 31,
            192 => bytes[1] == 168,
            _ => false,
        };
    }

    private static string? ExtractText(IDictionary<string, object?>? arguments, string name)
    {
        if (arguments == null || !arguments.TryGetValue(name, out object? value)) return null;
        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString(),
        };
    }
}
