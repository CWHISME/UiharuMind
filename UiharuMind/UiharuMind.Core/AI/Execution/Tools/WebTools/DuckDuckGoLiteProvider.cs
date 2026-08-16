using AngleSharp.Dom;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

internal sealed class DuckDuckGoLiteProvider : BaseSearchProvider
{
    protected override Uri DefaultReferer => new("https://lite.duckduckgo.com/");
    public override string Name => "DDG Lite";

    public override Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int maxCount, CancellationToken ct)
    {
        HttpRequestMessage req = CreateRequest(HttpMethod.Post, "https://lite.duckduckgo.com/lite/");
        req.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("q", query),
            new KeyValuePair<string, string>("kl", "wt-wt")
        });

        return SendAndParseAsync(req, doc => Parse(doc, maxCount), ct);
    }

    private static IEnumerable<SearchResultItem> Parse(IDocument doc, int maxCount)
    {
        return doc.QuerySelectorAll("a.result-link")
            .Take(maxCount)
            .Select(a => new SearchResultItem(
                Title: a.TextContent.Trim(),
                Url: CleanDdgRedirect(a.GetAttribute("href")),
                Snippet: a.Closest("tr")?.QuerySelector("td:nth-of-type(2)")?.TextContent.Trim() ?? ""
            ))
            .Where(x => x.Url.StartsWith("http"));
    }
}
