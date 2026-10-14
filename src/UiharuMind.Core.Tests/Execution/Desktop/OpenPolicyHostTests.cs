using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Desktop;

namespace UiharuMind.Core.Tests.Execution.Desktop;

/// <summary>
/// desktop.launch 的预检落在宿主上（ADR 0084，P0）：名单内应用、路径形状、URL 在被平台启动之前就拒
/// </summary>
public class OpenPolicyHostTests
{
    private readonly FakeDesktopPlatform _platform = new();
    private readonly DesktopHost _host;

    public OpenPolicyHostTests()
    {
        _host = new DesktopHost(_platform);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Terminal")] // 名单内（macOS 终端）
    [InlineData("cmd")] // 名单内（Windows cmd）
    [InlineData("System Settings")] // 名单内（系统设置）
    public async Task Open_BlockedApp_RefusesAndNeverStartsPlatform(string app)
    {
        AxActionException error = await Assert.ThrowsAsync<AxActionException>(() =>
            _host.OpenAsync(app, isFile: false, Token));
        Assert.Contains("off-limits", error.Message);
        Assert.Empty(_platform.Opened);
    }

    [Theory]
    [InlineData("/usr/bin/curl")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("https://example.com")]
    [InlineData("$HOME/evil.sh")]
    public async Task Open_PathOrUrl_RefusesAndNeverStartsPlatform(string target)
    {
        await Assert.ThrowsAsync<AxActionException>(() => _host.OpenAsync(target, isFile: false, Token));
        Assert.Empty(_platform.Opened);
    }

    [Fact]
    public async Task Open_ValidApp_StartsAndListsItsWindows()
    {
        _platform.AddWindow("TextEdit", "com.apple.TextEdit", "Note", new AxNode("window", "Note"));

        IReadOnlyList<DesktopWindow> opened = await _host.OpenAsync("TextEdit", isFile: false, Token);

        Assert.Equal([("TextEdit", false)], _platform.Opened);
        Assert.Contains(opened, window => window.Info.App == "TextEdit" && window.Blocked == null);
    }
}