using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

public static class WebSearchTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量,写死字面量迟早对不上</summary>
    public const string ToolName = "WebSearch";

    private static readonly FallbackSearchEngine Engine = new();

    public static AITool Create()
    {
        return AIFunctionFactory.Create(SearchAsync, ToolName,
            "Search the web for current information. Returns concise titles, URLs and summaries. " +
            $"If the summary is insufficient, use {WebFetchTool.ToolName} on a specific URL.");
    }

    private static async Task<string> SearchAsync(
        [Description("Exact search query.")] string query,
        [Description("Number of results (1-10, default 5).")] int count = 5,
        CancellationToken ct = default)
    {
        count = Math.Clamp(count, 1, 10);

        try
        {
            var items = await Engine.SearchAsync(query, count, ct);
            if (items.Count == 0)
                return "No results found from any available engine.";

            var sb = new StringBuilder($"Search results for \"{query}\":\n");
            for (int i = 0; i < items.Count; i++)
            {
                var r = items[i];
                sb.AppendLine($"{i + 1}. {r.Title}");
                sb.AppendLine($"   URL: {r.Url}");
                if (!string.IsNullOrWhiteSpace(r.Snippet))
                    sb.AppendLine($"   Summary: {r.Snippet}");
                sb.AppendLine();
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"[Search error] {ex.Message}";
        }
    }
}