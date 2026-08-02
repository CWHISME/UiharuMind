using UiharuMind.Core.AI.Agent.Tools.WebTools;

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
}
