using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死翻页的两件事：下一页优先回到出过前一页的引擎；换了引擎时不把前几页的地址再塞回来。
/// </summary>
public class SearchPageMemoryTests
{
    private static SearchResultItem Item(string url) => new("t", url, "");

    [Fact]
    public void NextPage_PrefersProviderOfPreviousPage()
    {
        SearchPageMemory memory = new();
        memory.Remember("q", 1, "Bing HTML", [Item("https://a")]);

        Assert.Equal("Bing HTML", memory.PreferredProvider("Q", 2));
        Assert.Null(memory.PreferredProvider("q", 1));
        Assert.Null(memory.PreferredProvider("other", 2));
    }

    [Fact]
    public void ExcludesOnlyEarlierPages()
    {
        SearchPageMemory memory = new();
        memory.Remember("q", 1, "A", [Item("https://a"), Item("https://b")]);
        memory.Remember("q", 2, "A", [Item("https://c")]);

        IReadOnlyList<SearchResultItem> page2 =
            memory.ExcludeEarlierPages("q", 2, [Item("https://b"), Item("https://c"), Item("https://d")]);

        Assert.Equal(["https://c", "https://d"], page2.Select(i => i.Url));
    }
}
