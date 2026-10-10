using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

public static class WebSearchTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量,写死字面量迟早对不上</summary>
    public const string ToolName = "WebSearch";

    private const int PageSize = 10;
    private const int MaxPage = 5;

    private static FallbackSearchEngine Engine => FallbackSearchEngine.Shared;

    public static AITool Create()
    {
        return AIFunctionFactory.Create(SearchAsync, ToolName,
            $"Search the web for current information. Returns up to {PageSize} titles, URLs and summaries per page. " +
            $"If the summary is insufficient, use {WebFetchTool.ToolName} on a specific URL. " +
            "Later pages are usually less relevant; rephrasing the query often works better than paging deep.");
    }

    private static async Task<string> SearchAsync(
        [Description("Exact search query.")] string query,
        [Description("Result page, 1-5 (default 1). Ask for the next page with the same query.")] int page = 1,
        CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, MaxPage);

        try
        {
            var items = await Engine.SearchAsync(query, page, PageSize, ct);
            if (items.Count == 0)
                return page == 1 ? "No results found from any available engine." : $"No more results beyond page {page - 1}.";

            var sb = new StringBuilder($"Search results for \"{query}\" (page {page}):\n");
            for (int i = 0; i < items.Count; i++)
            {
                var r = items[i];
                sb.AppendLine($"{(page - 1) * PageSize + i + 1}. {r.Title}");
                sb.AppendLine($"   URL: {r.Url}");
                if (!string.IsNullOrWhiteSpace(r.Snippet))
                    sb.AppendLine($"   Summary: {r.Snippet}");
                sb.AppendLine();
            }

            sb.Append(items.Count == PageSize && page < MaxPage
                ? $"More results: call again with page={page + 1}."
                : "End of results.");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"[Search error] {ex.Message}";
        }
    }
}