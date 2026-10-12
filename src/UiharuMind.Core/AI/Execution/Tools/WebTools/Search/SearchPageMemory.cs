/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 记住每个查询各页由哪个引擎给出、给了哪些地址。
/// 翻页时优先回到出过前一页的引擎；它挂了换引擎，排序全然不同，靠前几页的地址去重，免得把看过的再塞回来。
/// </summary>
internal sealed class SearchPageMemory
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private const int MaxQueries = 64;

    private readonly ConcurrentDictionary<string, QueryPages> _queries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 出过这个查询前一页的引擎
    /// </summary>
    /// <param name="query">查询词</param>
    /// <param name="page">要取的页码</param>
    /// <returns>引擎名；第一页或没记录为 null</returns>
    public string? PreferredProvider(string query, int page)
    {
        if (page <= 1 || !TryGet(query, out QueryPages? pages)) return null;
        lock (pages!)
        {
            return pages.Providers.TryGetValue(page - 1, out string? name) ? name : null;
        }
    }

    /// <summary>
    /// 去掉这个查询更靠前几页已经给过的地址
    /// </summary>
    /// <param name="query">查询词</param>
    /// <param name="page">本页页码</param>
    /// <param name="items">本页结果</param>
    /// <returns>去重后的结果</returns>
    public IReadOnlyList<SearchResultItem> ExcludeEarlierPages(string query, int page, IReadOnlyList<SearchResultItem> items)
    {
        if (page <= 1 || !TryGet(query, out QueryPages? pages)) return items;
        lock (pages!)
        {
            HashSet<string> seen = pages.Urls.Where(p => p.Key < page).SelectMany(p => p.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return items.Where(i => !seen.Contains(i.Url)).ToList();
        }
    }

    /// <summary>
    /// 记下这一页
    /// </summary>
    /// <param name="query">查询词</param>
    /// <param name="page">页码</param>
    /// <param name="provider">给出本页的引擎</param>
    /// <param name="items">本页结果</param>
    public void Remember(string query, int page, string provider, IReadOnlyList<SearchResultItem> items)
    {
        if (_queries.Count >= MaxQueries) Prune();
        QueryPages pages = _queries.AddOrUpdate(query, _ => new QueryPages(),
            (_, old) => old.Expires < DateTime.UtcNow ? new QueryPages() : old);
        lock (pages)
        {
            pages.Expires = DateTime.UtcNow + Ttl;
            pages.Providers[page] = provider;
            pages.Urls[page] = items.Select(i => i.Url).ToList();
        }
    }

    private bool TryGet(string query, out QueryPages? pages)
    {
        return _queries.TryGetValue(query, out pages) && pages.Expires >= DateTime.UtcNow;
    }

    private void Prune()
    {
        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<string, QueryPages> pair in _queries)
        {
            if (pair.Value.Expires < now) _queries.TryRemove(pair);
        }

        //全都没过期还是满了，清掉最旧的一半
        if (_queries.Count < MaxQueries) return;
        foreach (KeyValuePair<string, QueryPages> pair in _queries.OrderBy(p => p.Value.Expires).Take(MaxQueries / 2))
        {
            _queries.TryRemove(pair);
        }
    }

    private sealed class QueryPages
    {
        public DateTime Expires = DateTime.UtcNow + Ttl;
        public readonly Dictionary<int, string> Providers = new();
        public readonly Dictionary<int, List<string>> Urls = new();
    }
}
