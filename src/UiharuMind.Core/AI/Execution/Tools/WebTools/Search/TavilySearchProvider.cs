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
/// Tavily 搜索(自带 API key 的正规通路,免费额度可用)。
/// key 每次调用现读配置——设置页填入立即生效;没填 key 时兜底链会直接跳过本环(见 IsAvailable)。
/// </summary>
internal sealed class TavilySearchProvider : ISearchProvider
{
    private const string Endpoint = "https://api.tavily.com/search";

    public string Name => "Tavily";

    public bool IsAvailable => !string.IsNullOrWhiteSpace(AgentSettingConfig.Current.TavilyApiKey);

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(string query, int maxCount, CancellationToken ct)
    {
        string apiKey = AgentSettingConfig.Current.TavilyApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return [];

        //失败一律抛给兜底链:它负责记日志并落到下一环
        using HttpRequestMessage request = new(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { query, max_results = maxCount }),
            Encoding.UTF8, "application/json");

        using HttpResponseMessage response =
            await WebShared.Http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return Parse(json);
    }

    /// <summary>
    /// 解析 Tavily 响应({"results":[{"title","url","content"}]})
    /// </summary>
    /// <param name="json">响应 JSON</param>
    /// <returns>结果列表</returns>
    internal static List<SearchResultItem> Parse(string json)
    {
        List<SearchResultItem> items = [];
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out JsonElement results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (JsonElement result in results.EnumerateArray())
        {
            string url = WebShared.GetString(result, "url");
            if (url.Length == 0) continue;
            items.Add(new SearchResultItem(
                WebShared.GetString(result, "title"), url, WebShared.GetString(result, "content")));
        }

        return items;
    }
}
