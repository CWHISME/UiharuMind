using AngleSharp;
using UiharuMind.Core.AI.Execution.Search;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

internal sealed class DuckDuckGoHtmlProvider : BaseSearchProvider
{
    protected override Uri DefaultReferer => new("https://html.duckduckgo.com/");
    public override string Name => "DDG HTML";

    public override async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int maxCount, CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Post, "https://html.duckduckgo.com/html");
        req.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", query) });

        var doc = await DoSendAsync(req, ct);
        if (doc == null) return Array.Empty<SearchResultItem>();

        if (IsBlockedPage(doc)) return Array.Empty<SearchResultItem>();
        return doc.QuerySelectorAll("div.result")
            .Take(maxCount)
            .Select(r =>
            {
                var a = r.QuerySelector("a.result__a");
                var s = r.QuerySelector("a.result__snippet");
                return new SearchResultItem(
                    Title: a?.TextContent.Trim() ?? "",
                    Url: CleanDdgRedirect(a?.GetAttribute("href")),
                    Snippet: s?.TextContent.Trim() ?? ""
                );
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Title) && x.Url.StartsWith("http"))
            .ToList();
    }
}