using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 上传只取工作区与草稿目录里已有的文件；指到外面的软链也不行
/// </summary>
public sealed class BrowserUploadFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-upload-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly AgentPathResolver _paths;

    public BrowserUploadFilesTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "ws"));
        Directory.CreateDirectory(Path.Combine(_root, "draft"));
        Directory.CreateDirectory(Path.Combine(_root, "home"));
        File.WriteAllText(Path.Combine(_root, "ws", "cv.pdf"), "cv");
        File.WriteAllText(Path.Combine(_root, "draft", "chart.png"), "png");
        File.WriteAllText(Path.Combine(_root, "home", "id_rsa"), "secret");
        _paths = new AgentPathResolver(Path.Combine(_root, "ws"), Path.Combine(_root, "draft"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void WorkspaceAndDraftFiles_ResolveToAbsolutePaths()
    {
        IReadOnlyList<string> files = BrowserUploadFiles.Resolve("cv.pdf\n$DRAFT/chart.png\n", _paths);

        Assert.Equal([Path.Combine(_paths.WorkspaceRoot, "cv.pdf"), Path.Combine(_paths.DraftRoot, "chart.png")], files);
    }

    [Fact]
    public void FilesOutsideBothRoots_AreRefused()
    {
        BrowserActionException error = Assert.Throws<BrowserActionException>(() =>
            BrowserUploadFiles.Resolve(Path.Combine(_root, "home", "id_rsa"), _paths));

        Assert.Contains("workspace", error.Message);
    }

    [Fact]
    public void LinkInsideTheWorkspace_PointingOutside_IsRefused()
    {
        File.CreateSymbolicLink(Path.Combine(_paths.WorkspaceRoot, "key"), Path.Combine(_root, "home", "id_rsa"));

        Assert.Throws<BrowserActionException>(() => BrowserUploadFiles.Resolve("key", _paths));
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing.pdf")]
    [InlineData(".")]
    public void NothingToUpload_IsRefused(string value)
    {
        Assert.Throws<BrowserActionException>(() => BrowserUploadFiles.Resolve(value, _paths));
    }
}
