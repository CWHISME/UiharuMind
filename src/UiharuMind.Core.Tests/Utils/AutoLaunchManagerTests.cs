using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Utils;

public class AutoLaunchManagerTests
{
    [Theory]
    [InlineData("/Applications/UiharuMind.app/Contents/MacOS/UiharuMind.Desktop", "/Applications/UiharuMind.app")]
    [InlineData("/Users/a/My Apps/UiharuMind.app/Contents/MacOS/UiharuMind.Desktop", "/Users/a/My Apps/UiharuMind.app")]
    [InlineData("/Applications/UiharuMind.app", "/Applications/UiharuMind.app")]
    public void ResolveMacBundlePath_FindsBundle(string processPath, string expected)
    {
        Assert.Equal(expected, AutoLaunchManager.ResolveMacBundlePath(processPath));
    }

    [Theory]
    [InlineData("/usr/local/bin/uiharu")]
    [InlineData("/tmp/build/UiharuMind.Desktop")]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveMacBundlePath_OutsideBundle_IsNull(string? processPath)
    {
        Assert.Null(AutoLaunchManager.ResolveMacBundlePath(processPath));
    }

    [Fact]
    public void BuildMacArguments_InsideBundle_OpensApp()
    {
        Assert.Equal(
            ["/usr/bin/open", "-a", "/Applications/UiharuMind.app"],
            AutoLaunchManager.BuildMacArguments("/Applications/UiharuMind.app/Contents/MacOS/UiharuMind.Desktop"));
    }

    [Fact]
    public void BuildMacArguments_OutsideBundle_RunsExeDirectly()
    {
        Assert.Equal(["/tmp/build/UiharuMind.Desktop"],
            AutoLaunchManager.BuildMacArguments("/tmp/build/UiharuMind.Desktop"));
    }

    [Fact]
    public void BuildMacPlist_ContainsLabelAndRunAtLoad()
    {
        string plist = AutoLaunchManager.BuildMacPlist(
            AutoLaunchManager.MacPlistLabel, ["/usr/bin/open", "-a", "/Applications/UiharuMind.app"]);

        Assert.Contains("<string>com.cwhisme.uiharumind</string>", plist);
        Assert.Contains("<true/>", plist);
        Assert.Contains("<string>/usr/bin/open</string>", plist);
        Assert.Contains("<string>/Applications/UiharuMind.app</string>", plist);
    }

    [Fact]
    public void BuildMacPlist_EscapesXml()
    {
        string plist = AutoLaunchManager.BuildMacPlist("a&b", ["/tmp/a<b"]);
        Assert.Contains("a&amp;b", plist);
        Assert.Contains("/tmp/a&lt;b", plist);
    }

    [Fact]
    public void MacPlist_RoundTripsThroughFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"autolaunch-test-{Guid.NewGuid():N}");
        string plistPath = Path.Combine(dir, "LaunchAgents", AutoLaunchManager.MacPlistFileName);
        try
        {
            Assert.False(AutoLaunchManager.IsMacEnabled(plistPath));
            AutoLaunchManager.SetMacEnabled(plistPath, true, "/Applications/UiharuMind.app/Contents/MacOS/UiharuMind.Desktop");
            Assert.True(AutoLaunchManager.IsMacEnabled(plistPath));
            Assert.Contains("RunAtLoad", File.ReadAllText(plistPath));
            AutoLaunchManager.SetMacEnabled(plistPath, false, string.Empty);
            Assert.False(AutoLaunchManager.IsMacEnabled(plistPath));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BuildLinuxDesktopFile_QuotesExec()
    {
        string desktop = AutoLaunchManager.BuildLinuxDesktopFile("/opt/My App/uiharu");
        Assert.Contains("Type=Application", desktop);
        Assert.Contains("Exec=\"/opt/My App/uiharu\"", desktop);
        Assert.Contains("X-GNOME-Autostart-enabled=true", desktop);
    }

    [Fact]
    public void LinuxDesktopFile_RoundTripsThroughFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"autolaunch-test-{Guid.NewGuid():N}");
        string filePath = Path.Combine(dir, ".config", "autostart", AutoLaunchManager.LinuxDesktopFileName);
        try
        {
            Assert.False(AutoLaunchManager.IsLinuxEnabled(filePath));
            AutoLaunchManager.SetLinuxEnabled(filePath, true, "/opt/uiharu/UiharuMind.Desktop");
            Assert.True(AutoLaunchManager.IsLinuxEnabled(filePath));
            File.AppendAllText(filePath, "Hidden=true\n");
            Assert.False(AutoLaunchManager.IsLinuxEnabled(filePath));
            AutoLaunchManager.SetLinuxEnabled(filePath, false, string.Empty);
            Assert.False(AutoLaunchManager.IsLinuxEnabled(filePath));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BuildWindowsCommand_QuotesPath()
    {
        Assert.Equal("\"C:\\Program Files\\UiharuMind\\UiharuMind.Desktop.exe\"",
            AutoLaunchManager.BuildWindowsCommand("C:\\Program Files\\UiharuMind\\UiharuMind.Desktop.exe"));
    }

    [Theory]
    // 缺失就建
    [InlineData(null, "/Applications/UiharuMind.app", false, AutoLaunchManager.SyncDecision.Created)]
    // 已是当前程序就不动
    [InlineData("/Applications/UiharuMind.app", "/Applications/UiharuMind.app", true, AutoLaunchManager.SyncDecision.UpToDate)]
    // 登记的是别的路径但目标还有效：可能是手改的，不覆盖
    [InlineData("/Applications/Other.app", "/Applications/UiharuMind.app", true, AutoLaunchManager.SyncDecision.LeftAlone)]
    // 登记的是别的路径且目标已没了：过期，修
    [InlineData("/Applications/Old.app", "/Applications/UiharuMind.app", false, AutoLaunchManager.SyncDecision.Repaired)]
    public void DecideSync_MissingCurrentStaleOrForeign(
        string? registered, string current, bool targetExists, AutoLaunchManager.SyncDecision expected)
    {
        Assert.Equal(expected, AutoLaunchManager.DecideSync(
            registered, current, targetExists, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DecideSync_RespectsComparison()
    {
        Assert.Equal(AutoLaunchManager.SyncDecision.UpToDate, AutoLaunchManager.DecideSync(
            "/OPT/UIHARU", "/opt/uiharu", true, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(AutoLaunchManager.SyncDecision.LeftAlone, AutoLaunchManager.DecideSync(
            "/OPT/UIHARU", "/opt/uiharu", true, StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureMacEntry_CreatesRepairsAndLeavesAlone()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"autolaunch-test-{Guid.NewGuid():N}");
        string plistPath = Path.Combine(dir, "LaunchAgents", AutoLaunchManager.MacPlistFileName);
        string exePath = Path.Combine(dir, "UiharuMind.app", "Contents", "MacOS", "UiharuMind.Desktop");
        string bundlePath = Path.Combine(dir, "UiharuMind.app");
        try
        {
            Assert.Equal(AutoLaunchManager.SyncDecision.Created, AutoLaunchManager.EnsureMacEntry(plistPath, exePath));
            string created = File.ReadAllText(plistPath);
            Assert.Contains(bundlePath, created);

            // 已是当前程序：不动文件
            Assert.Equal(AutoLaunchManager.SyncDecision.UpToDate, AutoLaunchManager.EnsureMacEntry(plistPath, exePath));
            Assert.Equal(created, File.ReadAllText(plistPath));

            // 登记了不存在的旧路径：修
            AutoLaunchManager.SetMacEnabled(plistPath, true, "/Applications/Old.app/Contents/MacOS/Old");
            Assert.Equal(AutoLaunchManager.SyncDecision.Repaired, AutoLaunchManager.EnsureMacEntry(plistPath, exePath));
            Assert.Contains(bundlePath, File.ReadAllText(plistPath));

            // 登记了另一个有效位置：不动
            string otherExe = Path.Combine(dir, "Other.app", "Contents", "MacOS", "Other");
            Directory.CreateDirectory(Path.GetDirectoryName(otherExe)!);
            File.WriteAllText(otherExe, "x");
            AutoLaunchManager.SetMacEnabled(plistPath, true, otherExe);
            string foreign = File.ReadAllText(plistPath);
            Assert.Equal(AutoLaunchManager.SyncDecision.LeftAlone, AutoLaunchManager.EnsureMacEntry(plistPath, exePath));
            Assert.Equal(foreign, File.ReadAllText(plistPath));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReadMacRegisteredPath_ParsesBothForms()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"autolaunch-test-{Guid.NewGuid():N}");
        string plistPath = Path.Combine(dir, "x.plist");
        try
        {
            Assert.Null(AutoLaunchManager.ReadMacRegisteredPath(plistPath));
            AutoLaunchManager.SetMacEnabled(plistPath, true, "/Applications/UiharuMind.app/Contents/MacOS/UiharuMind.Desktop");
            Assert.Equal("/Applications/UiharuMind.app",
                AutoLaunchManager.ReadMacRegisteredPath(plistPath));
            AutoLaunchManager.SetMacEnabled(plistPath, true, "/tmp/build/UiharuMind.Desktop");
            Assert.Equal("/tmp/build/UiharuMind.Desktop",
                AutoLaunchManager.ReadMacRegisteredPath(plistPath));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LinuxExec_EscapesAndRoundTrips()
    {
        // Windows 文件系统不允许文件名里出现 '"',这条是 Linux 语义(用例专测它的转义),Windows 上跳过
        if (OperatingSystem.IsWindows()) return;

        Assert.Equal("/opt/a%%b\\\"c", AutoLaunchManager.QuoteLinuxExecArg("/opt/a%b\"c"));

        string dir = Path.Combine(Path.GetTempPath(), $"autolaunch-test-{Guid.NewGuid():N}");
        string filePath = Path.Combine(dir, ".config", "autostart", AutoLaunchManager.LinuxDesktopFileName);
        string exePath = Path.Combine(dir, "my%app\"dir", "UiharuMind.Desktop");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);
            File.WriteAllText(exePath, "x");
            Assert.Equal(AutoLaunchManager.SyncDecision.Created, AutoLaunchManager.EnsureLinuxEntry(filePath, exePath));
            Assert.Equal(exePath, AutoLaunchManager.ReadLinuxRegisteredPath(filePath));
            Assert.Equal(AutoLaunchManager.SyncDecision.UpToDate, AutoLaunchManager.EnsureLinuxEntry(filePath, exePath));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
