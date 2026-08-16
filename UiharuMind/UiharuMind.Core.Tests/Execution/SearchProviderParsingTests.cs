using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死 API 搜索响应的解析:字段缺失/形状异常时返回空而不是抛出——
/// 解析失败在兜底链里等价于空过,绝不能打断后续引擎。
/// </summary>
public class SearchProviderParsingTests
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

    [Fact]
    public void FirecrawlPage_ParsesMarkdown()
    {
        PageReadResult result = FirecrawlPageReader.Parse("""{"data":{"markdown":"# Title\ntext"}}""");

        Assert.Equal("# Title\ntext", result.Content);
        Assert.Null(result.Error);
    }

    [Fact]
    public void FirecrawlPage_EmptyOrMissingData_Fails()
    {
        Assert.Null(FirecrawlPageReader.Parse("""{"data":{"markdown":""}}""").Content);
        Assert.NotNull(FirecrawlPageReader.Parse("""{"success":false}""").Error);
    }

    /// <summary>
    /// 内网地址一律不许出门:Firecrawl 读不到,还会把地址泄露给第三方
    /// </summary>
    [Theory]
    [InlineData("http://localhost:8080/admin")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://[::1]:3000/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://172.16.3.9/")]
    [InlineData("http://172.31.255.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://100.101.102.103/")]
    [InlineData("http://nas/photos")]
    [InlineData("http://gitlab.internal/repo")]
    [InlineData("http://printer.local/")]
    public void PrivateHosts_AreRejectedByFirecrawl(string url)
    {
        Assert.True(WebShared.IsLocalOrPrivateHost(url));
        Assert.False(new FirecrawlPageReader().CanRead(url));
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("https://docs.firecrawl.dev/")]
    [InlineData("http://172.32.0.1/")] //刚好落在 172.16/12 之外
    [InlineData("http://8.8.8.8/")]
    public void PublicHosts_AreAcceptedByFirecrawl(string url)
    {
        Assert.False(WebShared.IsLocalOrPrivateHost(url));
        Assert.True(new FirecrawlPageReader().CanRead(url));
    }
}
