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
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Agent.Tools.WebTools;

/// <summary>
/// Tavily 搜索(自带 API key 的正规通路,免费额度可用)。
/// key 每次调用现读配置——设置页填入立即生效;未配置时直接空过,由后续引擎兜底。
/// 任何失败只记日志并空过,绝不打断兜底链。
/// </summary>
internal sealed class TavilySearchProvider : ISearchProvider
{
    private const string Endpoint = "https://api.tavily.com/search";

    public string Name => "Tavily";

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(string query, int maxCount, CancellationToken ct)
    {
        string apiKey = AgentSettingConfig.Current.TavilyApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return [];

        try
        {
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
        catch (Exception e)
        {
            Log.Warning($"Tavily search failed: {e.Message}");
            return [];
        }
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
            string url = GetString(result, "url");
            if (url.Length == 0) continue;
            items.Add(new SearchResultItem(GetString(result, "title"), url, GetString(result, "content")));
        }

        return items;
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }
}
