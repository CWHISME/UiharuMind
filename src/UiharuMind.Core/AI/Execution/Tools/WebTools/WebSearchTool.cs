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
            $"搜索网络获取最新信息。每页最多返回 {PageSize} 条标题、URL 和摘要。" +
            $"若摘要不够，用 {WebFetchTool.ToolName} 抓取具体 URL。" +
            "靠后的页面相关性通常更低；改写查询词往往比翻页更有效。");
    }

    private static async Task<string> SearchAsync(
        [Description("精确的搜索查询词。")] string query,
        [Description("结果页码，1-5（默认 1）。同一查询要更多结果时请求下一页。")] int page = 1,
        CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, MaxPage);

        try
        {
            var items = await Engine.SearchAsync(query, page, PageSize, ct);
            if (items.Count == 0)
                return page == 1 ? "所有可用的搜索引擎都没找到结果。" : $"第 {page - 1} 页之后再没有更多结果。";

            var sb = new StringBuilder($"「{query}」的搜索结果（第 {page} 页）：\n");
            for (int i = 0; i < items.Count; i++)
            {
                var r = items[i];
                sb.AppendLine($"{(page - 1) * PageSize + i + 1}. {r.Title}");
                sb.AppendLine($"   链接: {r.Url}");
                if (!string.IsNullOrWhiteSpace(r.Snippet))
                    sb.AppendLine($"   摘要: {r.Snippet}");
                sb.AppendLine();
            }

            sb.Append(items.Count == PageSize && page < MaxPage
                ? $"还有更多结果：用 page={page + 1} 再调一次。"
                : "结果结束。");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"[Search error] {ex.Message}";
        }
    }
}