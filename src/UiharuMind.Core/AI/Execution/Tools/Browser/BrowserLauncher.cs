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
/// 浏览器启动的统一入口：先按 Puppeteer 的系统检测起 Google Chrome，找不到（多半没装）
/// 再用 <see cref="BrowserLocator"/> 找 Microsoft Edge 补位；两个都没有就把话说明白。
/// 有窗口的 <see cref="PuppeteerBrowserHost"/> 与无头 <see cref="HeadlessPageRenderer"/> 共用这一套，
/// 免得两处各自写一遍回退逻辑。
/// </summary>
internal static class BrowserLauncher
{
    /// <summary>
    /// 用 <paramref name="launch"/> 起浏览器；传给它的 <c>null</c> 表示让 Puppeteer 自己找 Chrome，
    /// 非 <c>null</c> 是该填进 <c>ExecutablePath</c> 的补位浏览器路径（目前是 Edge）
    /// </summary>
    /// <param name="launch">真正启动浏览器的函数，返回启动好的浏览器</param>
    /// <param name="findFallback">找补位浏览器的函数（测试注入用；不传就用系统探测）</param>
    /// <returns>启动成功的浏览器</returns>
    internal static async Task<T> LaunchWithFallbackAsync<T>(
        Func<string?, Task<T>> launch,
        Func<string?>? findFallback = null)
    {
        try
        {
            return await launch(null);
        }
        catch (Exception e) when (e is PuppeteerException or ProcessException or TimeoutException)
        {
            string? fallback = (findFallback ?? BrowserLocator.TryFindEdge)();
            if (fallback == null)
            {
                throw new PuppeteerException(
                    "No Chromium browser available: could not find Google Chrome or Microsoft Edge in the usual places.",
                    e);
            }

            Log.Warning($"[Browser] Chrome unavailable ({e}); falling back to Microsoft Edge: {fallback}");
            try
            {
                return await launch(fallback);
            }
            catch (Exception second) when (second is PuppeteerException or ProcessException or TimeoutException)
            {
                throw new PuppeteerException(
                    $"Microsoft Edge at \"{fallback}\" could not start either. ({second.Message})", second);
            }
        }
    }

    /// <summary>
    /// 把 Chrome/Edge 的启动差异一次装进 <paramref name="options"/>，两个启动点口径一致：
    /// 独立档案目录（两个浏览器共写一个目录会互相污染，残留进程还会把新启动的请求接走）；
    /// Edge 不走 pipe 而走调试端口——Chromium 在 Windows 上会因兼容层/降权自我重启，
    /// 重启后的进程不继承 --remote-debugging-pipe 的 fd，窗口开着但 CDP 永远握不上手，
    /// 所以跳过重启，让最初进程就是最终提供服务的那个。
    /// </summary>
    /// <param name="options">启动参数；Pipe/Args/UserDataDir/ExecutablePath/Channel 在这里定</param>
    /// <param name="executablePath">补位浏览器路径；null 表示用 Puppeteer 系统检测的 Google Chrome</param>
    /// <param name="profileRoot">档案根目录（Chrome 与 Edge 会在下面各开一个子目录）</param>
    internal static void ApplyFallbackOptions(LaunchOptions options, string? executablePath, string profileRoot)
    {
        bool isEdge = executablePath != null;
        options.UserDataDir = Path.Combine(profileRoot, isEdge ? "Edge" : "Chrome");
        Directory.CreateDirectory(options.UserDataDir);
        options.Pipe = !isEdge;
        List<string> args = [.. options.Args ?? []];
        if (OperatingSystem.IsWindows())
        {
            // 提权宿主下 Chrome 同样会 relaunch 丢管道（同一 issue），所以 Chrome 分支也带上；Edge 再额外跳过兼容层
            args.Add("--do-not-de-elevate");
            if (isEdge) args.Add("--edge-skip-compat-layer-relaunch");
        }

        options.Args = [.. args];
        if (isEdge)
        {
            // 只填 ExecutablePath，不设 Channel：那是 Chrome 的 stable/beta/dev/canary 通道，
            // 与 ExecutablePath 并存时路径解析会被带回 Chrome
            options.ExecutablePath = executablePath;
        }
        else
        {
            options.Channel = ChromeReleaseChannel.Stable;
        }
    }
}