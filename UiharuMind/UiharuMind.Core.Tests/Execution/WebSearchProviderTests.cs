using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死 API 搜索响应的解析:字段缺失/形状异常时返回空而不是抛出——
/// 解析失败在兜底链里等价于空过,绝不能打断后续引擎。
/// </summary>
public class WebSearchProviderTests
{
    [Fact]
    public void Tavily_ParsesResults()
    {
        const string json = """
            {"results":[
                {"title":"T1","url":"https://a.example","content":"C1"},
                {"title":"T2","url":"https://b.example","content":"C2"},
                {"title":"no url entry","content":"skipped"}
            ]}
            """;

        List<SearchResultItem> items = TavilySearchProvider.Parse(json);

        Assert.Equal(2, items.Count);
        Assert.Equal(new SearchResultItem("T1", "https://a.example", "C1"), items[0]);
    }

    [Fact]
    public void Tavily_MissingResults_ReturnsEmpty()
    {
        Assert.Empty(TavilySearchProvider.Parse("""{"answer":"nothing"}"""));
    }

    [Fact]
    public void Brave_ParsesWebResults_AndRespectsMaxCount()
    {
        const string json = """
            {"web":{"results":[
                {"title":"B1","url":"https://a.example","description":"D1"},
                {"title":"B2","url":"https://b.example","description":"D2"},
                {"title":"B3","url":"https://c.example","description":"D3"}
            ]}}
            """;

        List<SearchResultItem> items = BraveSearchProvider.Parse(json, maxCount: 2);

        Assert.Equal(2, items.Count);
        Assert.Equal(new SearchResultItem("B1", "https://a.example", "D1"), items[0]);
    }

    [Fact]
    public void Brave_MissingWebSection_ReturnsEmpty()
    {
        Assert.Empty(BraveSearchProvider.Parse("""{"type":"error"}""", maxCount: 5));
    }

    [Fact]
    public void Firecrawl_ParsesWebResults_AndRespectsMaxCount()
    {
        const string json = """
            {"success":true,"data":{"web":[
                {"title":"F1","url":"https://a.example","description":"D1"},
                {"title":"F2","url":"https://b.example","description":"D2"},
                {"title":"F3","url":"https://c.example","description":"D3"}
            ]}}
            """;

        List<SearchResultItem> items = FirecrawlSearchProvider.Parse(json, maxCount: 2);

        Assert.Equal(2, items.Count);
        Assert.Equal(new SearchResultItem("F1", "https://a.example", "D1"), items[0]);
    }

    [Fact]
    public void Firecrawl_DataAsArray_IsAlsoAccepted()
    {
        const string json = """{"data":[{"title":"F1","url":"https://a.example","description":"D1"}]}""";

        Assert.Single(FirecrawlSearchProvider.Parse(json, maxCount: 5));
    }

    [Fact]
    public void Firecrawl_MissingData_ReturnsEmpty()
    {
        Assert.Empty(FirecrawlSearchProvider.Parse("""{"success":false,"error":"rate limited"}""", maxCount: 5));
    }

    /// <summary>
    /// Firecrawl 无 key 也能用,所以永远可用。需要 key 的引擎不在这里断言——
    /// 那取决于本机配置里填没填,钉死它等于让测试跟着开发机的设置走。
    /// </summary>
    [Fact]
    public void Firecrawl_IsAlwaysAvailable()
    {
        Assert.True(((ISearchProvider)new FirecrawlSearchProvider()).IsAvailable);
    }
}
