using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 搜索引擎兜底链:自带 API key 的正规通路优先(未配置 key 时秒过),
/// 爬页面的免费引擎殿后。单个引擎失败或空结果都只是落到下一环。
/// </summary>
internal sealed class FallbackSearchEngine
{
    private readonly ISearchProvider[] _chain =
    {
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
            try
            {
                var r = await p.SearchAsync(query, maxCount, ct);
                if (r.Count > 0) return r;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Warning($"Search provider '{p.Name}' failed: {e.Message}");
            }
        }

        return Array.Empty<SearchResultItem>();
    }
}
