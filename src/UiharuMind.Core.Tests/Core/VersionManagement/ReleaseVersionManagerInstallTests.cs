using System.IO.Compression;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Core.VersionManagement;

public class ReleaseVersionManagerInstallTests
{
    private sealed class TestVersion : ManagedVersionPackage
    {
    }

    // 只看安装流程：目录换好就算成功
    private sealed class TestVersionManager : ReleaseVersionManagerBase<TestVersion>
    {
        protected override string Owner => "owner";

        protected override string Repository => "repo";

        protected override ManagedVersionValidationResult ValidateInstalledVersion(TestVersion version) =>
            Directory.Exists(version.InstallDirectory)
                ? ManagedVersionValidationResult.Valid
                : new ManagedVersionValidationResult(false, "missing");
    }

    [Fact]
    public async Task ConcurrentInstall_SamePackage_DoesNotStampedeTempDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "uiharu-install-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string payloadDir = Path.Combine(root, "payload");
            Directory.CreateDirectory(payloadDir);
            // 几十个小文件拉宽解压窗口，让并发踩踏更容易在旧实现下暴露
            for (int i = 0; i < 30; i++)
            {
                File.WriteAllText(Path.Combine(payloadDir, $"data{i:D2}.bin"), i.ToString());
            }

            File.WriteAllText(Path.Combine(payloadDir, "server.exe"), "fake");
            string package = Path.Combine(root, "pkg.zip");
            ZipFile.CreateFromDirectory(payloadDir, package);

            var manager = new TestVersionManager();
            var version = new TestVersion
            {
                Name = "v1",
                PackageFilePath = package,
                InstallDirectory = Path.Combine(root, "installed", "v1")
            };

            // 同包并发安装（设置页手动装 + 下载队列自动装会同时进来）：
            // 门闩保证同一目录只有一份在解压，其余并发份等到时包已被删、走 fast-path 直接收敛；
            // 旧实现共用一个固定 ".installing" 目录，各线程互相删对方正在解压的目录 / 抢写文件句柄
            Task[] installs = Enumerable.Range(0, 4)
                .Select(_ => manager.InstallArchiveAsync(version))
                .ToArray();
            await Task.WhenAll(installs);

            Assert.True(Directory.Exists(version.InstallDirectory));
            Assert.True(File.Exists(Path.Combine(version.InstallDirectory, "server.exe")));
            Assert.Empty(Directory.EnumerateDirectories(
                Path.Combine(root, "installed"), "v1.installing*"));
            Assert.Empty(Directory.EnumerateDirectories(
                Path.Combine(root, "installed"), "v1.old*"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InstallArchive_ArchiveMissingAndNotInstalled_Throws()
    {
        string root = Path.Combine(Path.GetTempPath(), "uiharu-install-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var manager = new TestVersionManager();
            var version = new TestVersion
            {
                Name = "v1",
                PackageFilePath = Path.Combine(root, "missing.zip"),
                InstallDirectory = Path.Combine(root, "installed", "v1")
            };

            await Assert.ThrowsAsync<FileNotFoundException>(() => manager.InstallArchiveAsync(version));
            Assert.False(Directory.Exists(version.InstallDirectory));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ScanLocal_IgnoresTransientInstallDirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), "uiharu-scan-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            // 崩溃残留的半截临时目录与旧版备份，含完整的 server.exe 也不能算版本；
            // 唯一名目录可能正被另一线程安装中，扫描只跳过不删；固定名 .installing 必是历史残留，顺手删
            Directory.CreateDirectory(Path.Combine(root, "v1.installing.abc123"));
            File.WriteAllText(Path.Combine(root, "v1.installing.abc123", "server.exe"), "truncated");
            Directory.CreateDirectory(Path.Combine(root, "v1.old.def456"));
            File.WriteAllText(Path.Combine(root, "v1.old.def456", "server.exe"), "old");
            Directory.CreateDirectory(Path.Combine(root, "v0.installing"));
            File.WriteAllText(Path.Combine(root, "v0.installing", "server.exe"), "legacy");
            Directory.CreateDirectory(Path.Combine(root, "v2"));
            File.WriteAllText(Path.Combine(root, "v2", "server.exe"), "ok");

            var manager = new TestVersionManager();
            IReadOnlyList<TestVersion> versions = await manager.GetLocalVersionsAsync(root);

            Assert.Equal("v2", Assert.Single(versions).Name);
            // 唯一名残留保留但不在版本列表里；固定名 .installing 被删
            Assert.True(Directory.Exists(Path.Combine(root, "v1.installing.abc123")));
            Assert.True(Directory.Exists(Path.Combine(root, "v1.old.def456")));
            Assert.False(Directory.Exists(Path.Combine(root, "v0.installing")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}