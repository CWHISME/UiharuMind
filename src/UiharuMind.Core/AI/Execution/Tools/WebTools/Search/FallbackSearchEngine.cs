using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Resilience;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 搜索引擎兜底链:无 key 即可用的 Firecrawl 打头,其次是自带 API key 的正规通路
/// (未配置 key 时秒过),爬页面的免费引擎殿后。单个引擎失败或空结果都只是落到下一环,
/// 连续失败的引擎由 <see cref="ServiceCircuit"/> 暂时摘掉。
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

    private readonly SearchPageMemory _pages = new();

    /// <summary>链上的引擎,顺序即优先级</summary>
    public IReadOnlyList<ISearchProvider> Providers => _chain;

    /// <summary>
    /// 搜一页结果。翻页时优先回到出过前一页的引擎，换了引擎按前几页的地址去重
    /// </summary>
    /// <param name="query">查询词</param>
    /// <param name="page">页码，从 1 起</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>本页结果；全链走空为空</returns>
    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int page, int pageSize, CancellationToken ct)
    {
        int offset = (page - 1) * pageSize;
        string? preferred = _pages.PreferredProvider(query, page);
        IEnumerable<ISearchProvider> chain = preferred == null
            ? _chain
            : _chain.OrderBy(p => p.Name == preferred ? 0 : 1);

        foreach (var p in chain)
        {
            if (!p.IsAvailable)
            {
                Log.Debug($"[WebSearch] skip '{p.Name}': not configured");
                continue;
            }

            if (ServiceCircuit.IsTripped(p.Name, out TimeSpan cooldown))
            {
                Log.Debug($"[WebSearch] skip '{p.Name}': circuit open, {cooldown.TotalSeconds:F0}s left");
                continue;
            }

            try
            {
                var r = _pages.ExcludeEarlierPages(query, page, await p.SearchAsync(query, offset, pageSize, ct));
                if (r.Count > 0)
                {
                    ServiceCircuit.RecordSuccess(p.Name);
                    _pages.Remember(query, page, p.Name, r);
                    Log.Debug($"[WebSearch] hit '{p.Name}': {r.Count} results for \"{query}\" page {page}");
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
                if (ServiceCircuit.IsServiceLevelFailure(e)) ServiceCircuit.RecordFailure(p.Name, e.Message);
                Log.Warning($"[WebSearch] miss '{p.Name}': {e.Message}");
            }
        }

        Log.Warning($"[WebSearch] all providers failed for \"{query}\"");
        return Array.Empty<SearchResultItem>();
    }
}
