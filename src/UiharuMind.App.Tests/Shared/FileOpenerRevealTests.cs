using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 「在文件夹中显示」的路径口径：产物目录名里混着 / 时，explorer 会把 /select 整条吞掉、只弹桌面。
/// 锁住规范化与参数构造，顺带覆盖「目标本身是目录」和「文件已被删」两条回落。
/// </summary>
public sealed class FileOpenerRevealTests
{
    [Fact]
    public void ResolveRevealTarget_NormalizesMixedSeparators_AndSelectsTheFile()
    {
        using Sandbox sandbox = new();
        string file = sandbox.CreateFile("segment/id8", "note.txt");

        FileOpener.RevealTarget? target = FileOpener.ResolveRevealTarget(file);
        Assert.NotNull(target);
        Assert.Equal(Path.GetFullPath(file), target.Path);
        Assert.True(target.SelectFile);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(file)), target.Directory);
        Assert.Equal($"/select,\"{Path.GetFullPath(file)}\"", FileOpener.WindowsRevealArguments(target));

        if (!OperatingSystem.IsWindows()) return;
        Assert.DoesNotContain("/", target.Path);
        Assert.DoesNotContain("segment/id8", FileOpener.WindowsRevealArguments(target));
    }

    [Fact]
    public void ResolveRevealTarget_OpensTheDirectoryItself_WhenTargetIsADirectory()
    {
        using Sandbox sandbox = new();

        FileOpener.RevealTarget? target = FileOpener.ResolveRevealTarget(sandbox.Root);
        Assert.NotNull(target);
        Assert.False(target.SelectFile);
        Assert.Equal($"\"{Path.GetFullPath(sandbox.Root)}\"", FileOpener.WindowsRevealArguments(target));
    }

    [Fact]
    public void ResolveRevealTarget_FallsBackToDirectory_WhenFileIsGone()
    {
        using Sandbox sandbox = new();
        string gone = Path.Combine(sandbox.Root, "removed.txt");

        FileOpener.RevealTarget? target = FileOpener.ResolveRevealTarget(gone);
        Assert.NotNull(target);
        Assert.False(target.SelectFile);
        Assert.Equal(Path.GetFullPath(sandbox.Root), target.Directory);
        Assert.Equal($"\"{Path.GetFullPath(sandbox.Root)}\"", FileOpener.WindowsRevealArguments(target));
    }

    [Fact]
    public void ResolveRevealTarget_ReturnsNull_WhenDirectoryIsGone()
    {
        string gone = Path.Combine(Path.GetTempPath(), $"uiharu-reveal-{Guid.NewGuid():N}", "note.txt");
        Assert.Null(FileOpener.ResolveRevealTarget(gone));
    }

    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"uiharu-reveal-{Guid.NewGuid():N}");

        public Sandbox() => Directory.CreateDirectory(Root);

        /// <summary>在子目录里造一个文件；子目录用 / 拼，复现草稿目录的路径形态</summary>
        public string CreateFile(string relativeDirectory, string fileName)
        {
            string directory = Path.Combine(Root, relativeDirectory);
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, fileName);
            File.WriteAllText(file, "x");
            return file;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
