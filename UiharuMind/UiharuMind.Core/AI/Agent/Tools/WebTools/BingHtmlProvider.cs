using AngleSharp;
using AngleSharp.Dom;
using UiharuMind.Core.AI.Agent.Search;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Agent.Tools.WebTools;

internal sealed class BingHtmlProvider : BaseSearchProvider
{
    protected override Uri DefaultReferer => new("https://www.bing.com/");
    public override string Name => "Bing HTML";

    public override async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int maxCount, CancellationToken ct)
    {
        var uri = $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}&setlang=en";
        using var req = CreateRequest(HttpMethod.Get, uri);

        var doc = await DoSendAsync(req, ct);
        if (doc == null) return Array.Empty<SearchResultItem>();

        if (doc.QuerySelector("#b_sydConvCont, .b_caption.b_noRedirect") != null)
            return Array.Empty<SearchResultItem>(); // Bing 拦截

        return doc.QuerySelectorAll(".b_algo")
            .Take(maxCount)
            .Select(algo =>
            {
                var link = algo.QuerySelector("h2 > a");
                var desc = algo.QuerySelector(".b_caption p");
                return new SearchResultItem(
                    Title: link?.TextContent.Trim() ?? "",
                    Url: link?.GetAttribute("href")?.Trim() ?? "",
                    Snippet: desc?.TextContent.Trim() ?? ""
                );
            })
            .Where(x => Uri.IsWellFormedUriString(x.Url, UriKind.Absolute))
            .ToList();
    }
}