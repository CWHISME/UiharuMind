using System.Net;
using System.Text;
using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 要本机 Chrome 的标签页行为，显式跑。无头、临时档案，不碰真实档案也不抢屏幕
/// </summary>
public class PuppeteerTabLiveTests
{
    private static readonly string Scratch = Path.Combine(Path.GetTempPath(), "uiharu-live-" + Guid.NewGuid().ToString("N")[..8]);

    private static Task<IBrowser> LaunchAsync() => Puppeteer.LaunchAsync(new LaunchOptions
    {
        Browser = SupportedBrowser.Chrome,
        Channel = ChromeReleaseChannel.Stable,
        Headless = true,
        UserDataDir = Path.Combine(Scratch, "profile-" + Guid.NewGuid().ToString("N")[..8]),
        Pipe = true,
    });

    [Fact(Explicit = true)]
    public async Task Live_FilePickerOpenedByAnAction_IsClosedAndReported()
    {
        await using IBrowser browser = await LaunchAsync();
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

    [Fact(Explicit = true)]
    public async Task Live_ClickedDownload_LandsInTheSessionsDirectory()
    {
        await using IBrowser browser = await LaunchAsync();
        string directory = Path.Combine(Scratch, "downloads");
        PuppeteerTab tab = new((await browser.PagesAsync())[0]) { DownloadDirectory = directory };
        await PuppeteerDownloads.EnableAsync(browser, Path.Combine(Scratch, "staging"), _ => tab);
        const string html = "<a download=\"notes.txt\" href=\"data:text/plain,hello\">下载</a>";
        await tab.NavigateAsync("data:text/html;charset=utf-8," + Uri.EscapeDataString(html), CancellationToken.None);

        BrowserPageView view = await tab.ActAsync(new BrowserActionRequest(EBrowserAction.Click, "下载", ""),
            CancellationToken.None);

        string saved = Path.Combine(directory, "notes.txt");
        Assert.Contains(view.Notes, note => note.Contains(saved));
        Assert.Equal("hello", await File.ReadAllTextAsync(saved, TestContext.Current.CancellationToken));
    }

    [Fact(Explicit = true)]
    public async Task Live_NavigatingToAFile_DownloadsItInsteadOfFailing()
    {
        using HttpListener server = new();
        int port = Random.Shared.Next(20000, 40000);
        server.Prefixes.Add($"http://127.0.0.1:{port}/");
        server.Start();
        _ = Task.Run(async () =>
        {
            HttpListenerContext context = await server.GetContextAsync();
            context.Response.AddHeader("Content-Disposition", "attachment; filename=\"../../report.csv\"");
            byte[] body = Encoding.UTF8.GetBytes("a,b\n1,2\n");
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        });

        await using IBrowser browser = await LaunchAsync();
        string directory = Path.Combine(Scratch, "nav-downloads");
        PuppeteerTab tab = new((await browser.PagesAsync())[0]) { DownloadDirectory = directory };
        await PuppeteerDownloads.EnableAsync(browser, Path.Combine(Scratch, "staging"), _ => tab);

        BrowserPageView view = await tab.NavigateAsync($"http://127.0.0.1:{port}/report", CancellationToken.None);

        // 文件名带着 ../ 也只落在会话目录里（Chrome 与我们各洗一道，具体洗成什么不管）
        string saved = Assert.Single(Directory.GetFiles(directory));
        Assert.EndsWith("report.csv", saved);
        Assert.Contains(view.Notes, note => note.Contains("file download"));
        Assert.Contains(view.Notes, note => note.Contains(saved));
        Assert.Equal("a,b\n1,2\n", await File.ReadAllTextAsync(saved, TestContext.Current.CancellationToken));
    }
}
