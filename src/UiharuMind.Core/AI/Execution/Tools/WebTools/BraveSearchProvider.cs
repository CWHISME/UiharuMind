/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// Brave Search(自带 API key 的正规通路,免费档可用)。
/// key 每次调用现读配置;没填 key 时兜底链会直接跳过本环(见 IsAvailable)。
/// </summary>
internal sealed class BraveSearchProvider : ISearchProvider
{
    private const string Endpoint = "https://api.search.brave.com/res/v1/web/search";

    public string Name => "Brave";

    public bool IsAvailable => !string.IsNullOrWhiteSpace(AgentSettingConfig.Current.BraveSearchApiKey);

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(string query, int maxCount, CancellationToken ct)
    {
        string apiKey = AgentSettingConfig.Current.BraveSearchApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return [];

        //失败一律抛给兜底链:它负责记日志并落到下一环
        string url = $"{Endpoint}?q={Uri.EscapeDataString(query)}&count={maxCount}";
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Add("X-Subscription-Token", apiKey.Trim());
        request.Headers.Accept.ParseAdd("application/json");

        using HttpResponseMessage response =
            await WebShared.Http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return Parse(json, maxCount);
    }

    /// <summary>
    /// 解析 Brave 响应({"web":{"results":[{"title","url","description"}]}})
    /// </summary>
    /// <param name="json">响应 JSON</param>
    /// <param name="maxCount">结果上限</param>
    /// <returns>结果列表</returns>
    internal static List<SearchResultItem> Parse(string json, int maxCount)
    {
        List<SearchResultItem> items = [];
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("web", out JsonElement web) ||
            !web.TryGetProperty("results", out JsonElement results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (JsonElement result in results.EnumerateArray())
        {
            if (items.Count >= maxCount) break;
            string url = WebShared.GetString(result, "url");
            if (url.Length == 0) continue;
            items.Add(new SearchResultItem(
                WebShared.GetString(result, "title"), url, WebShared.GetString(result, "description")));
        }

        return items;
    }
}
