using AngleSharp;
using UiharuMind.Core.AI.Agent.Search;

namespace UiharuMind.Core.AI.Agent.Tools.WebTools;

internal sealed class DuckDuckGoLiteProvider : BaseSearchProvider
{
    protected override Uri DefaultReferer => new("https://lite.duckduckgo.com/");
    public override string Name => "DDG Lite";

    public override async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int maxCount, CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Post, "https://lite.duckduckgo.com/lite/");
        req.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("q", query),
            new KeyValuePair<string, string>("kl", "wt-wt")
        });

        var doc = await DoSendAsync(req, ct);
        if (doc == null) return Array.Empty<SearchResultItem>();
        
        if (IsBlockedPage(doc)) return Array.Empty<SearchResultItem>();
        return doc.QuerySelectorAll("a.result-link")
            .Take(maxCount)
            .Select(a => new SearchResultItem(
                Title: a.TextContent.Trim(),
                Url: CleanDdgRedirect(a.GetAttribute("href")),
                Snippet: a.Closest("tr")?.QuerySelector("td:nth-of-type(2)")?.TextContent.Trim() ?? ""
            ))
            .Where(x => x.Url.StartsWith("http"))
            .ToList();
    }
}