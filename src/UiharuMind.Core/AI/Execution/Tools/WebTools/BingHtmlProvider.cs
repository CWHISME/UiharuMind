using AngleSharp.Dom;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

internal sealed class BingHtmlProvider : BaseSearchProvider
{
    protected override Uri DefaultReferer => new("https://www.bing.com/");
    public override string Name => "Bing HTML";

    public override Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int maxCount, CancellationToken ct)
    {
        string uri = $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}&setlang=en";
        return SendAndParseAsync(CreateRequest(HttpMethod.Get, uri), maxCount, ct);
    }

    /// <summary>Bing 的拦截页有自己的特征</summary>
    protected override bool IsBlocked(IDocument doc)
    {
        return base.IsBlocked(doc) || doc.QuerySelector("#b_sydConvCont, .b_caption.b_noRedirect") != null;
    }

    protected override IEnumerable<SearchResultItem> ParseResults(IDocument doc, int maxCount)
    {
        return doc.QuerySelectorAll(".b_algo")
            .Take(maxCount)
            .Select(algo =>
            {
                IElement? link = algo.QuerySelector("h2 > a");
                IElement? desc = algo.QuerySelector(".b_caption p");
                return new SearchResultItem(
                    Title: link?.TextContent.Trim() ?? "",
                    Url: link?.GetAttribute("href")?.Trim() ?? "",
                    Snippet: desc?.TextContent.Trim() ?? ""
                );
            })
            .Where(x => Uri.IsWellFormedUriString(x.Url, UriKind.Absolute));
    }
}
