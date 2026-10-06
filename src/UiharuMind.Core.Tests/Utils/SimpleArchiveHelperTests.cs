using System.Diagnostics;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 解压：.tar.gz 要解到 tar 里的文件（而不是解出一个无名的 tar），软链接照留
/// </summary>
public class SimpleArchiveHelperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uiharu-archive-{Guid.NewGuid():N}");

    public SimpleArchiveHelperTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task TarGz_ExtractsInnerFiles_AndSymlinks()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "用系统 tar 造包，Windows 上软链接要权限");
        string source = Path.Combine(_root, "src", "llama-b1");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "libx.1.0.dylib"), "lib", TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(Path.Combine(source, "libx.1.dylib"), "libx.1.0.dylib");
        string archive = Path.Combine(_root, "llama-b1-bin-macos-arm64.tar.gz");
        using (Process tar = Process.Start("tar", ["-czf", archive, "-C", Path.Combine(_root, "src"), "llama-b1"])!)
            await tar.WaitForExitAsync(TestContext.Current.CancellationToken);

        string target = Path.Combine(_root, "out");
        await SimpleArchiveHelper.ExtractArchiveAsync(archive, target, true, TestContext.Current.CancellationToken);

        Assert.Equal("lib", await File.ReadAllTextAsync(Path.Combine(target, "llama-b1", "libx.1.dylib"),
            TestContext.Current.CancellationToken));
        Assert.NotNull(new FileInfo(Path.Combine(target, "llama-b1", "libx.1.dylib")).LinkTarget);
        Assert.False(File.Exists(archive));
        Assert.Empty(Directory.GetFiles(_root, "*.tar"));
    }
}
