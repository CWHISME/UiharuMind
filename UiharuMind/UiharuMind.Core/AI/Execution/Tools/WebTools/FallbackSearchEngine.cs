using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 搜索引擎兜底链:无 key 即可用的 Firecrawl 打头,其次是自带 API key 的正规通路
/// (未配置 key 时秒过),爬页面的免费引擎殿后。单个引擎失败或空结果都只是落到下一环。
/// </summary>
internal sealed class FallbackSearchEngine
{
    private readonly ISearchProvider[] _chain =
    {
        new FirecrawlSearchProvider(),
        new TavilySearchProvider(),
        new BraveSearchProvider(),
        new DuckDuckGoLiteProvider(),
        new DuckDuckGoHtmlProvider(),
        new BingHtmlProvider()
    };

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int maxCount, CancellationToken ct)
    {
        foreach (var p in _chain)
        {
            if (!p.IsAvailable)
            {
                Log.Debug($"[WebSearch] skip '{p.Name}': not configured");
                continue;
            }

            try
            {
                var r = await p.SearchAsync(query, maxCount, ct);
                if (r.Count > 0)
                {
                    Log.Debug($"[WebSearch] hit '{p.Name}': {r.Count} results for \"{query}\"");
                    return r;
                }

                Log.Warning($"[WebSearch] miss '{p.Name}': no results for \"{query}\"");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Warning($"[WebSearch] miss '{p.Name}': {e.Message}");
            }
        }

        Log.Warning($"[WebSearch] all providers failed for \"{query}\"");
        return Array.Empty<SearchResultItem>();
    }
}
