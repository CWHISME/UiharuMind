using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 扒页面引擎的解析逻辑。
///
/// 这里只钉住<b>不依赖线上页面</b>的两件事:拦截页要认出来、跳转链接要还原。
/// "选择器还认不认得真实结果页"不在这里管——那件事靠设置页的实测按钮,
/// 它会把真抓回来的标题和 URL 摆出来,比断言"解析出了 3 条"更能说明问题。
/// </summary>
public class SerpParsingRegressionTests
{
    /// <summary>拦截页要认出来并返回空,不能把验证码页面当结果解析</summary>
    [Fact]
    public async Task Bing_BlockedPage_YieldsNothing()
    {
        const string html = """
            <html><body><div id="b_sydConvCont">verify</div>
            <li class="b_algo"><h2><a href="https://a.example">T</a></h2></li></body></html>
            """;

        Assert.Empty(await new BingHtmlProvider().ParseHtmlAsync(html, maxCount: 5));
    }

    /// <summary>DDG 的结果链接裹在 /l/?uddg= 跳转里,不还原就全是 duckduckgo.com 自己</summary>
    [Fact]
    public async Task DdgLite_UnwrapsRedirectLinks()
    {
        const string html = """
            <html><body><table>
              <tr><td>1.</td><td><a class="result-link" href="https://a.example/x">First hit</a></td></tr>
              <tr><td></td><td>Snippet for the first hit.</td></tr>
              <tr><td>2.</td><td><a class="result-link"
                  href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fb.example%2Fy&amp;rut=abc">Second hit</a></td></tr>
            </table></body></html>
            """;

        IReadOnlyList<SearchResultItem> items = await new DuckDuckGoLiteProvider().ParseHtmlAsync(html, maxCount: 5);

        Assert.Equal(2, items.Count);
        Assert.Equal("https://a.example/x", items[0].Url);
        Assert.Equal("First hit", items[0].Title);
    }
}
