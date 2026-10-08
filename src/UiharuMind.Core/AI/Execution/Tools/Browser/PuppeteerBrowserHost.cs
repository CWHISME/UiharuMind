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
    private readonly SemaphoreSlim _gate = new(1, 1); //起浏览器、开标签页排队，免得并发调用各起一个
    private readonly ConcurrentDictionary<string, PuppeteerTab> _tabs = new();
    private readonly bool _headless;
    private IBrowser? _browser;

    private PuppeteerBrowserHost(bool headless)
    {
        _headless = headless;
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

    private async Task<PuppeteerTab> GetTabAsync(string sessionId)
    {
        if (_tabs.TryGetValue(sessionId, out PuppeteerTab? existing) && !existing.IsClosed) return existing;

        await _gate.WaitAsync();
        try
        {
            if (_tabs.TryGetValue(sessionId, out existing) && !existing.IsClosed) return existing;

            IBrowser browser = await EnsureBrowserAsync();
            PuppeteerTab tab = new(await browser.NewPageAsync());
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
        return browser;
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
