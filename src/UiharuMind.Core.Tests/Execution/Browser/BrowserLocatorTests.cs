using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// Edge 可执行文件的跨平台探测：候选路径按平台给得对不对、返回的一定是真实存在的文件
/// </summary>
public class BrowserLocatorTests
{
    [Fact]
    public void Windows_Candidates_PointAtMicrosoftEdgeUnderKnownProgramFiles()
    {
        string[] candidates = BrowserLocator.EdgeExecutableCandidates(EBrowserPlatform.Windows);

        Assert.NotEmpty(candidates);
        foreach (string candidate in candidates)
        {
            Assert.EndsWith(Path.Combine("Microsoft", "Edge", "Application", "msedge.exe"), candidate);
        }
    }

    [Fact]
    public void MacOS_Candidates_AreTheStandardApp()
    {
        string[] candidates = BrowserLocator.EdgeExecutableCandidates(EBrowserPlatform.MacOS);

        Assert.Equal(["/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"], candidates);
    }

    [Fact]
    public void Linux_Candidates_CoverOptAndUsrBin()
    {
        string[] candidates = BrowserLocator.EdgeExecutableCandidates(EBrowserPlatform.Linux);

        Assert.Contains("/opt/microsoft/msedge/msedge", candidates);
        Assert.Contains("/usr/bin/microsoft-edge", candidates);
        Assert.Contains("/usr/bin/microsoft-edge-stable", candidates);
    }

    [Fact]
    public void TryFindEdge_ReturnsAnExistingFileOrNull()
    {
        string? edge = BrowserLocator.TryFindEdge();

        if (edge != null) Assert.True(File.Exists(edge));
    }
}