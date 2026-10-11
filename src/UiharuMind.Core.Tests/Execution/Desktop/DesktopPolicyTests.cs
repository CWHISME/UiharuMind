using UiharuMind.Core.AI.Execution.Tools.Desktop;

namespace UiharuMind.Core.Tests.Execution.Desktop;

/// <summary>
/// desktop.open 的预检（ADR 0084，P0）：名单内的应用不能开、路径/URL 不当应用名、文件分支必须带路径解析器
/// </summary>
public class DesktopPolicyTests
{
    [Theory]
    [InlineData("Terminal")] // com.apple.Terminal
    [InlineData("terminal")]
    [InlineData("iTerm")] // com.googlecode.iterm2
    [InlineData("Ghostty")] // com.mitchellh.ghostty
    [InlineData("cmd")] // cmd.exe
    [InlineData("PowerShell")] // powershell.exe
    [InlineData("pwsh")]
    [InlineData("System Settings")] // SystemSettings.exe
    [InlineData("System Preferences")] // com.apple.systempreferences
    [InlineData("Keychain Access")] // com.apple.keychainaccess
    [InlineData("1Password")] // 1Password.exe
    [InlineData("KeePassXC")]
    [InlineData("UiharuMind")] // UiharuMind.Desktop.exe / com.cwhisme.uiharumind
    [InlineData("Windows Terminal")] // WindowsTerminal.exe
    public void BlockedApps_AreRefused(string name)
    {
        Assert.NotNull(DesktopPolicy.ValidateAppOpen(name));
    }

    [Theory]
    [InlineData("TextEdit")]
    [InlineData("Calculator")]
    [InlineData("Safari")]
    [InlineData("Notepad")]
    [InlineData("访达")]
    public void OrdinaryApps_AreAllowed(string name)
    {
        Assert.Null(DesktopPolicy.ValidateAppOpen(name));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://localhost:8080/hello")]
    [InlineData("file:///etc/passwd")]
    public void Urls_AreRefused(string name)
    {
        Assert.NotNull(DesktopPolicy.ValidateAppOpen(name));
    }

    [Theory]
    [InlineData("/usr/bin/curl")]
    [InlineData("/tmp/x.sh")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("./start.sh")]
    [InlineData("Payload.exe")]
    [InlineData("$HOME/evil")]
    public void PathShapes_AreRefusedAsAppNames(string name)
    {
        Assert.NotNull(DesktopPolicy.ValidateAppOpen(name));
        Assert.True(DesktopPolicy.LooksLikePathTarget(name));
    }

    [Fact]
    public void Empty_IsRefused()
    {
        Assert.NotNull(DesktopPolicy.ValidateAppOpen("   "));
    }

    [Fact]
    public void FileTarget_WithoutPathResolver_IsRefused()
    {
        Assert.NotNull(DesktopPolicy.FileTargetError("/etc/passwd", hasPathResolver: false));
        Assert.NotNull(DesktopPolicy.FileTargetError("$DRAFT/x.txt", hasPathResolver: false));
        Assert.NotNull(DesktopPolicy.FileTargetError("report.md", hasPathResolver: false));
    }

    [Fact]
    public void FileTarget_WithPathResolver_PassesToResolution()
    {
        Assert.Null(DesktopPolicy.FileTargetError("/etc/passwd", hasPathResolver: true));
        Assert.Null(DesktopPolicy.FileTargetError("$DRAFT/x.txt", hasPathResolver: true));
    }

    [Fact]
    public void FileTarget_PlainAppName_IsNotAPath_EvenWithoutResolver()
    {
        Assert.Null(DesktopPolicy.FileTargetError("Notepad", hasPathResolver: false));
        Assert.Null(DesktopPolicy.FileTargetError("TextEdit", hasPathResolver: false));
        Assert.False(DesktopPolicy.LooksLikePathTarget("TextEdit"));
    }
}