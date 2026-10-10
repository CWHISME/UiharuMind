using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死无头浏览器这一环的受理边界：浏览器起不来、内网地址都安静跳过，不计入失败。
/// </summary>
public class BrowserPageReaderTests
{
    private sealed class FakeRenderer(bool available, string html) : IHtmlRenderer
    {
        public bool IsAvailable => available;

        public Task<(string Url, string Html)> RenderAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult((url, html));
    }

    [Fact]
    public void SkipsWhenBrowserUnavailable_OrPrivateHost()
    {
        Assert.False(new BrowserPageReader(new FakeRenderer(false, "")).CanRead("https://example.com"));

        BrowserPageReader reader = new(new FakeRenderer(true, ""));
        Assert.False(reader.CanRead("http://127.0.0.1:8080/"));
        Assert.True(reader.CanRead("https://example.com"));
    }

    [Fact]
    public async Task ExtractsTextFromRenderedHtml()
    {
        const string html = "<html><body><article><h1>Rendered</h1><p>Body written by script.</p></article></body></html>";

        PageReadResult result = await new BrowserPageReader(new FakeRenderer(true, html))
            .ReadAsync("https://example.com", CancellationToken.None);

        Assert.Contains("Body written by script.", result.Content);
    }

    [Fact(Explicit = true)]
    public async Task Live_RendersScriptOnlyPage()
    {
        (_, string html) = await HeadlessPageRenderer.Shared.RenderAsync("https://quotes.toscrape.com/js/", CancellationToken.None);
        Assert.Contains("class=\"quote\"", html);
    }
}
