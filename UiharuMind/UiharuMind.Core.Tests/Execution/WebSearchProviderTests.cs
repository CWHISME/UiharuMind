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

    /// <summary>
    /// Firecrawl 的业务错误（HTTP 200 + <c>success:false</c>）必须抛异常而不是返回空
    /// ——空结果会被兜底链当成"正常搜到 0 条"，错误原文静默丢掉，模型拿到假成功。
    /// </summary>
    [Fact]
    public void Firecrawl_BusinessError_ThrowsInsteadOfSilentEmpty()
    {
        const string json = """{"success":false,"error":"rate limited"}""";

        var ex = Assert.Throws<HttpRequestException>(() => FirecrawlSearchProvider.EnsureSuccess(json));
        Assert.Contains("rate limited", ex.Message);
    }

    [Fact]
    public void Firecrawl_NoBusinessError_DoesNotThrow()
    {
        // 无 success 字段或 success 为 true 都不算业务错误
        FirecrawlSearchProvider.EnsureSuccess("""{"data":{"web":[]}}""");
        FirecrawlSearchProvider.EnsureSuccess("""{"success":true,"data":[]}""");
    }

    /// <summary>
    /// 解析器本身：不带 error 的畸形响应（缺 data）仍返回空——那只是"这个查询没有数据"，
    /// 不构成服务故障。业务错误由 <see cref="FirecrawlSearchProvider.EnsureSuccess"/> 提前拦截。
    /// </summary>
    [Fact]
    public void Firecrawl_MissingData_ReturnsEmpty()
    {
        Assert.Empty(FirecrawlSearchProvider.Parse("""{"foo":"bar"}""", maxCount: 5));
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
