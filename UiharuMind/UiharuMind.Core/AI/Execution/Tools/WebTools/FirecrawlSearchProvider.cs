/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// Firecrawl 搜索。无 key 也能用(见 <see cref="FirecrawlClient"/>),所以排在兜底链首位;
/// 限额耗尽/服务异常都只记日志并空过,由后续引擎接手。
/// </summary>
internal sealed class FirecrawlSearchProvider : ISearchProvider
{
    public string Name => "Firecrawl";

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(string query, int maxCount, CancellationToken ct)
    {
        //失败一律抛给兜底链:它负责记日志并落到下一环,这里再吞一次只会让日志出现两条
        string json = await FirecrawlClient
            .PostAsync("search", new { query, limit = maxCount }, ct)
            .ConfigureAwait(false);
        return Parse(json, maxCount);
    }

    /// <summary>
    /// 解析 Firecrawl 响应({"data":{"web":[{"title","url","description"}]}})
    /// </summary>
    /// <param name="json">响应 JSON</param>
    /// <param name="maxCount">结果上限</param>
    /// <returns>结果列表</returns>
    internal static List<SearchResultItem> Parse(string json, int maxCount)
    {
        List<SearchResultItem> items = [];
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data)) return items;

        // data 正常是 {"web":[...]};只要一种 source 时服务端也可能直接给数组
        JsonElement results = data.ValueKind == JsonValueKind.Array
            ? data
            : data.TryGetProperty("web", out JsonElement web) ? web : default;
        if (results.ValueKind != JsonValueKind.Array) return items;

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
