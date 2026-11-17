using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 「先 Chrome、起不来就补位 Edge」的回退决策：launch 被调几次、第二次用哪个路径、两个都没有时报什么
/// </summary>
public class BrowserLauncherTests
{
    [Fact]
    public async Task ChromeLaunchSucceeds_EdgeIsNeverTried()
    {
        var calls = new List<string?>();
        Task<string> launch(string? path)
        {
            calls.Add(path);
            return Task.FromResult("chrome");
        }

        string result = await BrowserLauncher.LaunchWithFallbackAsync(launch, () => "/edge/msedge");

        Assert.Equal("chrome", result);
        Assert.Equal(new string?[] { null }, calls);
    }

    [Fact]
    public async Task ChromeMissing_EdgeFound_LaunchesWithTheEdgePath()
    {
        var calls = new List<string?>();
        Task<string> launch(string? path)
        {
            calls.Add(path);
            if (path == null) throw new PuppeteerException("Could not find Google Chrome executable");
            return Task.FromResult("edge");
        }

        string result = await BrowserLauncher.LaunchWithFallbackAsync(launch, () => "/edge/msedge");

        Assert.Equal("edge", result);
        Assert.Equal(new string?[] { null, "/edge/msedge" }, calls);
    }

    [Fact]
    public async Task ChromeAndEdgeMissing_SaysNeitherIsThere()
    {
        Task<string> launch(string? path) => throw new PuppeteerException("Could not find Google Chrome executable");

        PuppeteerException ex = await Assert.ThrowsAsync<PuppeteerException>(() =>
            BrowserLauncher.LaunchWithFallbackAsync(launch, () => null));

        Assert.Contains("Chrome", ex.Message);
        Assert.Contains("Edge", ex.Message);
    }

    [Fact]
    public async Task EdgeAlsoFails_SaysWhichEdgePathFailed()
    {
        Task<string> launch(string? path) =>
            throw new PuppeteerException(path == null ? "no chrome" : "msedge crashed on start");

        PuppeteerException ex = await Assert.ThrowsAsync<PuppeteerException>(() =>
            BrowserLauncher.LaunchWithFallbackAsync(launch, () => "/edge/msedge"));

        Assert.Contains("/edge/msedge", ex.Message);
    }

    [Fact]
    public async Task ChromeTimedOut_EdgeFound_LaunchesWithTheEdgePath()
    {
        var calls = new List<string?>();
        Task<string> launch(string? path)
        {
            calls.Add(path);
            if (path == null) throw new TimeoutException("launch timed out");
            return Task.FromResult("edge");
        }

        string result = await BrowserLauncher.LaunchWithFallbackAsync(launch, () => "/edge/msedge");

        Assert.Equal("edge", result);
        Assert.Equal(new string?[] { null, "/edge/msedge" }, calls);
    }

    [Fact]
    public void EdgePath_GetsPortNotPipe_AndSkipsWindowsRelaunch()
    {
        LaunchOptions options = new() { Args = ["--no-first-run"] };

        BrowserLauncher.ApplyFallbackOptions(options, @"C:\edge\msedge.exe", @"C:\profiles");

        Assert.False(options.Pipe);
        Assert.Equal(@"C:\edge\msedge.exe", options.ExecutablePath);
        Assert.Null(options.Channel);
        Assert.Contains("--edge-skip-compat-layer-relaunch", options.Args);
        Assert.Equal(Path.Combine(@"C:\profiles", "Edge"), options.UserDataDir);
        if (OperatingSystem.IsWindows()) Assert.Contains("--do-not-de-elevate", options.Args);
    }

    [Fact]
    public void ChromePath_KeepsPipeAndStableChannel_AndSeparateProfile()
    {
        LaunchOptions options = new() { Args = ["--no-first-run"] };

        BrowserLauncher.ApplyFallbackOptions(options, null, @"C:\profiles");

        Assert.True(options.Pipe);
        Assert.Null(options.ExecutablePath);
        Assert.Equal(ChromeReleaseChannel.Stable, options.Channel);
        Assert.Equal(Path.Combine(@"C:\profiles", "Chrome"), options.UserDataDir);
    }
}