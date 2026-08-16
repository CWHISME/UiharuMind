using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 搜索引擎兜底链:无 key 即可用的 Firecrawl 打头,其次是自带 API key 的正规通路
/// (未配置 key 时秒过),爬页面的免费引擎殿后。单个引擎失败或空结果都只是落到下一环,
/// 连续失败的引擎由 <see cref="WebServiceCircuit"/> 暂时摘掉。
/// </summary>
internal sealed class FallbackSearchEngine
{
    /// <summary>全局共用的一条链。设置页的健康面板要看的就是它,不能另起一份</summary>
    public static FallbackSearchEngine Shared { get; } = new();

    private readonly ISearchProvider[] _chain =
    {
        new FirecrawlSearchProvider(),
        new TavilySearchProvider(),
        new BraveSearchProvider(),
        new DuckDuckGoLiteProvider(),
        new BingHtmlProvider()
    };

    /// <summary>链上的引擎,顺序即优先级</summary>
    public IReadOnlyList<ISearchProvider> Providers => _chain;

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

            if (WebServiceCircuit.IsTripped(p.Name, out TimeSpan cooldown))
            {
                Log.Debug($"[WebSearch] skip '{p.Name}': circuit open, {cooldown.TotalSeconds:F0}s left");
                continue;
            }

            try
            {
                var r = await p.SearchAsync(query, maxCount, ct);
                if (r.Count > 0)
                {
                    WebServiceCircuit.RecordSuccess(p.Name);
                    Log.Debug($"[WebSearch] hit '{p.Name}': {r.Count} results for \"{query}\"");
                    return r;
                }

                //空结果不计入熔断:冷门查询本来就可能一条都搜不到,不是引擎坏了
                Log.Warning($"[WebSearch] miss '{p.Name}': no results for \"{query}\"");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                if (WebServiceCircuit.IsServiceLevelFailure(e)) WebServiceCircuit.RecordFailure(p.Name, e.Message);
                Log.Warning($"[WebSearch] miss '{p.Name}': {e.Message}");
            }
        }

        Log.Warning($"[WebSearch] all providers failed for \"{query}\"");
        return Array.Empty<SearchResultItem>();
    }
}
