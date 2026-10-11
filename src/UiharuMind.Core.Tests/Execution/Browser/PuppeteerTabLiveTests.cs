using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 要本机 Chrome 的标签页行为，显式跑。无头、临时档案，不碰真实档案也不抢屏幕
/// </summary>
public class PuppeteerTabLiveTests
{
    [Fact(Explicit = true)]
    public async Task Live_FilePickerOpenedByAnAction_IsClosedAndReported()
    {
        string profile = Path.Combine(Path.GetTempPath(), "uiharu-live-" + Guid.NewGuid().ToString("N")[..8]);
        await using IBrowser browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Browser = SupportedBrowser.Chrome,
            Channel = ChromeReleaseChannel.Stable,
            Headless = true,
            UserDataDir = profile,
            Pipe = true,
        });
        PuppeteerTab tab = new((await browser.PagesAsync())[0]);
        const string html = "<button onclick=\"document.getElementById('f').click()\">上传附件</button>" +
                            "<input type=file id=f style='display:none'><button onclick=\"document.title='ok'\">别的</button>";
        await tab.NavigateAsync("data:text/html;charset=utf-8," + Uri.EscapeDataString(html), CancellationToken.None);

        BrowserPageView upload = await tab.ActAsync(new BrowserActionRequest(EBrowserAction.Click, "上传附件", ""),
            CancellationToken.None);
        BrowserPageView other = await tab.ActAsync(new BrowserActionRequest(EBrowserAction.Click, "别的", ""),
            CancellationToken.None);

        Assert.Contains(upload.Notes, note => note.Contains("file picker"));
        Assert.DoesNotContain(other.Notes, note => note.Contains("file picker"));
        Assert.Equal("ok", other.Title);
    }
}
