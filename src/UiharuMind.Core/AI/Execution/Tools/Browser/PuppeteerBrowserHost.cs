/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 用 PuppeteerSharp 驱动用户已装的 Chrome：第一次用到才启动，专用档案落在 <see cref="AppPaths.Data.Browser"/>
/// （登录态跨会话、跨重启保留），每个会话一个标签页（<see cref="PuppeteerTab"/>）。
/// 默认有窗口，用户看得见、接得过手（登录、验证码）；冒烟与无人值守设 <c>UIHARU_BROWSER_HEADLESS=1</c>。
/// </summary>
public sealed class PuppeteerBrowserHost : IBrowserHost
{
    private static readonly TimeSpan IdleTabLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan HandedOverLifetime = TimeSpan.FromHours(2); //接管卡挂着没人理，也不能永远占着
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);
    private static readonly string DownloadStaging = Path.Combine(AppPaths.Cache.Root, "BrowserDownloads"); //下载先按 guid 落这里

    private readonly SemaphoreSlim _gate = new(1, 1); //起浏览器、开标签页、回收排队，免得并发调用各起一个
    private readonly ConcurrentDictionary<string, PuppeteerTab> _tabs = new();
    private readonly ConcurrentDictionary<string, string> _downloadDirectories = new(); //会话 id → 下载目录
    private readonly Timer _idleSweep; //闲置回收；留着引用，免得计时器被回收
    private readonly bool _headless;
    private IBrowser? _browser;
    private IPage? _spare; //浏览器起来时自带的空白页：头一个会话用它，免得多出一个空窗口

    private PuppeteerBrowserHost(bool headless)
    {
        _headless = headless;
        _idleSweep = new Timer(_ => _ = SweepIdleAsync(), null, SweepInterval, SweepInterval);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
    }

    /// <summary>全应用共用的一个</summary>
    public static PuppeteerBrowserHost Shared { get; } =
        new(Environment.GetEnvironmentVariable("UIHARU_BROWSER_HEADLESS") == "1");

    /// <inheritdoc />
    public async Task<BrowserPageView> NavigateAsync(string sessionId, string url, CancellationToken cancellationToken) =>
        await (await GetTabAsync(sessionId)).NavigateAsync(url, cancellationToken);

    /// <inheritdoc />
    public async Task<BrowserPageView> SnapshotAsync(string sessionId, CancellationToken cancellationToken) =>
        await (await GetTabAsync(sessionId)).SnapshotAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<BrowserPageView> ActAsync(string sessionId, BrowserActionRequest request,
        CancellationToken cancellationToken) =>
        await (await GetTabAsync(sessionId)).ActAsync(request, cancellationToken);

    /// <inheritdoc />
    public async Task<BrowserTextView> ReadTextAsync(string sessionId, CancellationToken cancellationToken) =>
        await (await GetTabAsync(sessionId)).ReadTextAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<BrowserScreenshot> ScreenshotAsync(string sessionId, CancellationToken cancellationToken) =>
        await (await GetTabAsync(sessionId)).ScreenshotAsync(cancellationToken);

    /// <inheritdoc />
    public bool HasWindow => !_headless;

    /// <inheritdoc />
    /// <remarks>这个会话还没开过页面就先开一个空白窗口：接管可能正是「请你先登录 X」。
    /// 拿到的标签页恰好刚被关掉（用户关了窗口）就再开一个</remarks>
    public async Task<bool> ShowAsync(string sessionId)
    {
        if (_headless) return false;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (await (await GetTabAsync(sessionId)).ShowAsync()) return true;
        }

        return false;
    }

    /// <inheritdoc />
    public BrowserElement? PeekElement(string sessionId, string target) =>
        OpenTab(sessionId)?.Peek(target) is { } node ? new BrowserElement(node.Role, node.Name.Trim(), node.Secret) : null;

    /// <inheritdoc />
    public void UseDownloadDirectory(string sessionId, string directory)
    {
        _downloadDirectories[sessionId] = directory;
        if (_tabs.TryGetValue(sessionId, out PuppeteerTab? tab)) tab.DownloadDirectory = directory;
    }

    /// <inheritdoc />
    public string? PeekUrl(string sessionId) => OpenTab(sessionId)?.Url;

    // 只认已经开着的，不为回查新开页面
    private PuppeteerTab? OpenTab(string sessionId) =>
        _tabs.TryGetValue(sessionId, out PuppeteerTab? tab) && !tab.IsClosed ? tab : null;

    private async Task<PuppeteerTab> GetTabAsync(string sessionId)
    {
        if (_tabs.TryGetValue(sessionId, out PuppeteerTab? existing) && !existing.IsClosed) return existing;

        await _gate.WaitAsync();
        try
        {
            if (_tabs.TryGetValue(sessionId, out existing) && !existing.IsClosed) return existing;

            IBrowser browser = await EnsureBrowserAsync();
            // 一个会话一个窗口而不是标签页：Chrome 只给前台标签页跑渲染帧，后台标签页里等「位置稳定」永远等不到，
            // 并行的子代理各自点不动（实测）。各开一个窗口，每个都是前台
            IPage page = _spare is { IsClosed: false } spare
                ? spare
                : await browser.NewPageAsync(new CreatePageOptions { Type = CreatePageType.Window });
            _spare = null;
            PuppeteerTab tab = new(page) { DownloadDirectory = _downloadDirectories.GetValueOrDefault(sessionId, string.Empty) };
            tab.Closed += () => _tabs.TryRemove(new KeyValuePair<string, PuppeteerTab>(sessionId, tab));
            _tabs[sessionId] = tab;
            return tab;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IBrowser> EnsureBrowserAsync()
    {
        if (_browser is { IsConnected: true } running) return running;

        Directory.CreateDirectory(AppPaths.Data.Browser);
        IBrowser browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Browser = SupportedBrowser.Chrome,
            Channel = ChromeReleaseChannel.Stable,
            Headless = _headless,
            UserDataDir = AppPaths.Data.Browser,
            // 有窗口时跟着窗口走；无头时给个桌面尺寸，免得网站按手机排版
            DefaultViewport = _headless ? new ViewPortOptions { Width = 1280, Height = 900 } : null,
            Args = ["--no-first-run", "--no-default-browser-check"],
            // 走管道而不是调试端口：应用被强杀、崩溃时管道一断，Chrome 跟着退，不留后台孤儿进程占着档案
            Pipe = true,
        });
        // 用户把浏览器整个关了：标签页记录作废，下次调用重新起
        browser.Disconnected += (_, _) => _tabs.Clear();
        _browser = browser;
        _spare = (await browser.PagesAsync()).FirstOrDefault(p => p.Url == "about:blank");
        await PuppeteerDownloads.EnableAsync(browser, DownloadStaging, FindDownloadTab);
        return browser;
    }

    // 认不出框架（新开的标签页里点出来的下载）就归最近动过手的会话
    private PuppeteerTab? FindDownloadTab(string frameId) =>
        _tabs.Values.FirstOrDefault(tab => tab.OwnsFrame(frameId)) ??
        _tabs.Values.Where(tab => !tab.IsClosed).MaxBy(tab => tab.LastUsed);

    // 标签页闲着超过十分钟就关（子代理跑完的、会话删掉的都会剩下一个），接管中的放宽到两小时；一个都不剩时浏览器也关——
    // 登录态在档案里，下次用到再起，不白占内存
    private async Task SweepIdleAsync()
    {
        try
        {
            foreach ((string sessionId, PuppeteerTab tab) in _tabs)
            {
                // 用户正在里面接手（登录、过二次验证）的不回收，哪怕已经过了十分钟
                TimeSpan lifetime = tab.IsHandedOver ? HandedOverLifetime : IdleTabLifetime;
                if (DateTime.UtcNow - tab.LastUsed < lifetime) continue;
                if (_tabs.TryRemove(new KeyValuePair<string, PuppeteerTab>(sessionId, tab))) await tab.CloseAsync();
            }

            if (!_tabs.IsEmpty || _browser is not { IsConnected: true }) return;
            await _gate.WaitAsync();
            try
            {
                if (_tabs.IsEmpty && _browser is { IsConnected: true } browser)
                {
                    _browser = null;
                    await browser.CloseAsync();
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[Browser] idle sweep failed: {e.Message}");
        }
    }

    // 正常关浏览器才会把 cookie 写回档案，下次登录态还在
    private void Shutdown()
    {
        try
        {
            _browser?.CloseAsync().Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception e)
        {
            Log.Warning($"[Browser] close on exit failed: {e.Message}");
        }
    }
}
