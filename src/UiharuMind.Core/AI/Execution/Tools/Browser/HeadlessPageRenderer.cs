/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 把地址渲染成执行过脚本的 HTML
/// </summary>
public interface IHtmlRenderer
{
    /// <summary>当前能不能用：上次起浏览器失败（多半没装 Chrome）后冷却期内为 false</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 打开地址并取渲染后的 HTML
    /// </summary>
    /// <param name="url">地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>最终地址与 HTML</returns>
    Task<(string Url, string Html)> RenderAsync(string url, CancellationToken cancellationToken);
}

/// <summary>
/// 把一个地址渲染成 HTML 的无头 Chrome，给 WebFetch 兜底读 JS 渲染页用。
/// 与 <see cref="PuppeteerBrowserHost"/> 刻意分开：不带登录态（临时档案，按进程隔离，多实例互不锁档案）、
/// 不弹窗口、一次一页读完即关，所以也不走浏览器工具的审批。闲置一阵整个退出，不白占内存。
/// </summary>
public sealed class HeadlessPageRenderer : IHtmlRenderer
{
    private const int NavigationTimeoutMs = 20_000;
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan LaunchRetryDelay = TimeSpan.FromMinutes(10); //没装 Chrome 时别每次都白试一遍

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _profileDir = Path.Combine(Path.GetTempPath(), $"UiharuMind-fetch-{Environment.ProcessId}");
    private readonly Timer _idleSweep; //留着引用，免得计时器被回收
    private IBrowser? _browser;
    private int _inFlight;
    private DateTime _lastUsed = DateTime.UtcNow;
    private DateTime _launchBlockedUntil = DateTime.MinValue;

    private HeadlessPageRenderer()
    {
        _idleSweep = new Timer(_ => _ = SweepIdleAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
    }

    /// <summary>全应用共用的一个</summary>
    public static HeadlessPageRenderer Shared { get; } = new();

    /// <inheritdoc />
    public bool IsAvailable => DateTime.UtcNow >= _launchBlockedUntil;

    /// <inheritdoc />
    /// <remarks>等文档就绪、网络稍静后取 HTML，然后关掉页面</remarks>
    public async Task<(string Url, string Html)> RenderAsync(string url, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _inFlight);
        try
        {
            IBrowser browser = await EnsureBrowserAsync(cancellationToken);
            await using IPage page = await browser.NewPageAsync();
            await page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 900 });
            using CancellationTokenRegistration closeOnCancel = cancellationToken.Register(() => _ = page.CloseAsync());

            try
            {
                await page.GoToAsync(url, new NavigationOptions
                {
                    Timeout = NavigationTimeoutMs,
                    WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                });
            }
            catch (NavigationException e) when (e.InnerException is TimeoutException ||
                                                e.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug($"[WebFetch] headless still loading {url}, reading what is there");
            }

            // SPA 要等脚本把正文画出来；长连接的页面永远静不下来，等不到照常往下读
            try
            {
                await page.WaitForNetworkIdleAsync(new WaitForNetworkIdleOptions { IdleTime = 500, Timeout = 5000 });
            }
            catch (Exception e) when (e is TimeoutException or PuppeteerException)
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            return (page.Url, await page.GetContentAsync());
        }
        finally
        {
            _lastUsed = DateTime.UtcNow;
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private async Task<IBrowser> EnsureBrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsConnected: true } running) return running;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_browser is { IsConnected: true } existing) return existing;
            if (!IsAvailable) throw new InvalidOperationException("Chrome could not be started recently.");

            try
            {
                _browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Browser = SupportedBrowser.Chrome,
                    Channel = ChromeReleaseChannel.Stable,
                    Headless = true,
                    UserDataDir = _profileDir,
                    Args = ["--no-first-run", "--no-default-browser-check"],
                    Pipe = true,
                });
                return _browser;
            }
            catch (Exception e)
            {
                _launchBlockedUntil = DateTime.UtcNow + LaunchRetryDelay;
                Log.Warning($"[WebFetch] headless Chrome failed to start: {e.Message}");
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SweepIdleAsync()
    {
        if (_browser is not { IsConnected: true } || Volatile.Read(ref _inFlight) > 0 ||
            DateTime.UtcNow - _lastUsed < IdleLifetime)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true } browser && Volatile.Read(ref _inFlight) == 0)
            {
                _browser = null;
                await browser.CloseAsync();
                DeleteProfile();
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[WebFetch] headless idle close failed: {e.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Shutdown()
    {
        try
        {
            _browser?.CloseAsync().Wait(TimeSpan.FromSeconds(3));
            DeleteProfile();
        }
        catch (Exception e)
        {
            Log.Warning($"[WebFetch] headless close on exit failed: {e.Message}");
        }
    }

    private void DeleteProfile()
    {
        try
        {
            if (Directory.Exists(_profileDir)) Directory.Delete(_profileDir, true);
        }
        catch (IOException)
        {
        }
    }
}
