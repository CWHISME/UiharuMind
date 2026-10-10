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
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 浏览器调用里<b>任何权限档都要用户点一次</b>的那几类，是浏览器版的「越界写入贯穿三档」：
/// <list type="bullet">
/// <item>导航到本机、内网与非 http(s) 地址——它们够得着用户网络里本不对外的东西（路由器后台、本地服务）。
/// 只看地址字面，不解析 DNS：解析到内网的公网域名拦不住，与越界写入不解析软链同一个取舍。</item>
/// <item>接管：那张卡就是「请你接手」，点「完成了」之前没有可放行的——它等的是用户的手，不是许可。</item>
/// </list>
/// 往密码框打字不在这里：它不问，工具当场拒（见 <c>PuppeteerTab.Actions</c>），点过「本会话允许」也拦得住。
/// </summary>
internal static class BrowserRisk
{
    /// <summary>
    /// 这次调用是否在完全自动档下也要审批
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <returns>要审批为 true；不是浏览器调用、或导航没给地址（参数绑定当场失败，什么也打不开）为 false</returns>
    public static bool RequiresApprovalInAllModes(FunctionCallContent call) => call.Name switch
    {
        BrowserTools.HandoffToolName => true,
        BrowserTools.NavigateToolName => ExtractUrl(call.Arguments) is { Length: > 0 } url && IsLocalTarget(url),
        _ => false,
    };

    /// <summary>
    /// 地址是否指向本机、内网或非 http(s)
    /// </summary>
    /// <param name="url">地址；没写协议的按 https 理解（与导航工具一致）</param>
    /// <returns>是为 true</returns>
    internal static bool IsLocalTarget(string url)
    {
        string trimmed = url.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal) &&
            !trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)) return false;
        if (uri.Scheme == "about") return false; //about:blank 之类，什么也够不着
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return true;
        if (uri.IsLoopback) return true;

        string host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? address) && IsPrivate(address);
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

    private static string? ExtractUrl(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("url", out object? value)) return null;
        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString(),
        };
    }
}
