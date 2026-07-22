using UiharuMind.Core.AI.Agent.Tools.WebTools;

internal sealed class FallbackSearchEngine
{
    private readonly ISearchProvider[] _chain =
    {
        new DuckDuckGoLiteProvider(),
        new DuckDuckGoHtmlProvider(),
        new BingHtmlProvider()
    };

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int maxCount, CancellationToken ct)
    {
        foreach (var p in _chain)
        {
            var r = await p.SearchAsync(query, maxCount, ct);
            if (r.Count > 0) return r;
        }
        return Array.Empty<SearchResultItem>();
    }
}