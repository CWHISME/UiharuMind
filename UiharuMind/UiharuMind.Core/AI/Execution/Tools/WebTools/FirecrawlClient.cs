/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// Firecrawl v2 的请求出口(搜索与正文抓取共用)。
/// key <b>可以不填</b>——Firecrawl 支持无 key 调用,按来源 IP 限额,超限返回 429;
/// 填了 key 则额度更高。所以它排在两条兜底链的最前面,失败自然落到下一环。
/// </summary>
internal static class FirecrawlClient
{
    private const string BaseUrl = "https://api.firecrawl.dev/v2/";

    /// <summary>
    /// POST 一个 v2 端点并返回响应正文
    /// </summary>
    /// <param name="path">端点相对路径,如 "search" / "scrape"</param>
    /// <param name="payload">请求体,序列化为 JSON</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>响应 JSON 字符串</returns>
    public static async Task<string> PostAsync(string path, object payload, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, BaseUrl + path);

        string apiKey = AgentSettingConfig.Current.FirecrawlApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await WebShared.Http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
