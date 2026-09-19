using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// Write 去掉 <c>overwrite</c> 后的行为:直接覆盖、自动备份、只留最近若干份。
/// 备份根一律注入临时目录,不污染真实全局缓存。
/// </summary>
public class WriteBackupTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-writebak-").FullName;
    private readonly string _backupRoot;
    private readonly PermissiveFileAccessTools _tools;

    public WriteBackupTests()
    {
        _backupRoot = Path.Combine(_dir, "backups");
        _tools = new PermissiveFileAccessTools(_dir, new FileBackupStore(_backupRoot));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }

    /// <summary>新建文件没有上一版,结果里不应提备份,备份区应空无一物</summary>
    [Fact]
    public async Task Write_NewFile_NoBackupReported()
    {
        string result = await _tools.Write("new.txt", "hello\n", TestContext.Current.CancellationToken);

        Assert.Equal("Saved 'new.txt' (2 lines).", result);
        Assert.Equal("hello\n", await File.ReadAllTextAsync(Path.Combine(_dir, "new.txt"), TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(_backupRoot));
    }

    /// <summary>
    /// 覆盖已有文件直接成功,结果带出备份路径,备份与覆盖前逐字节一致(含 BOM)。
    /// </summary>
    [Fact]
    public async Task Write_ExistingFile_OverwritesAndBacksUp()
    {
        string path = Path.Combine(_dir, "target.txt");
        byte[] original = [0xEF, 0xBB, 0xBF, .. "old\n"u8];
        await File.WriteAllBytesAsync(path, original, TestContext.Current.CancellationToken);

        string result = await _tools.Write("target.txt", "new\n", TestContext.Current.CancellationToken);

        Assert.Equal("new\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Contains("backed up to", result);
        string backupPath = result[(result.IndexOf("backed up to '", StringComparison.Ordinal) + 14)..^2];
        Assert.True(File.Exists(backupPath), $"备份文件应存在:{backupPath}");
        Assert.Equal(original, await File.ReadAllBytesAsync(backupPath, TestContext.Current.CancellationToken));
    }

    /// <summary>连续覆盖只留最近份:kept=2 时写 3 次,桶里剩 v1/v2 两份,v0 被清掉</summary>
    [Fact]
    public async Task Write_BackupPruning_KeepsLatestOnly()
    {
        PermissiveFileAccessTools tools =
            new(_dir, new FileBackupStore(_backupRoot, keptPerFile: 2));
        await File.WriteAllTextAsync(Path.Combine(_dir, "rolling.txt"), "v0\n", TestContext.Current.CancellationToken);
        await tools.Write("rolling.txt", "v1\n", TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        await tools.Write("rolling.txt", "v2\n", TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        await tools.Write("rolling.txt", "v3\n", TestContext.Current.CancellationToken);

        string[] backups = Directory.GetFiles(Path.Combine(_backupRoot,
            Directory.GetDirectories(_backupRoot)[0]), "*.bak");
        Assert.Equal(2, backups.Length);
        string[] contents = await Task.WhenAll(backups.Select(path => File.ReadAllTextAsync(path)));
        Assert.Equal(["v1\n", "v2\n"], contents.OrderBy(x => x).ToArray());
        Assert.Equal("v3\n", await File.ReadAllTextAsync(Path.Combine(_dir, "rolling.txt"), TestContext.Current.CancellationToken));
    }

    /// <summary>备份失败永不抛:备份根被文件占住时返回 null,调用方照常无备份</summary>
    [Fact]
    public async Task FileBackupStore_Failure_ReturnsNullInsteadOfThrowing()
    {
        string blocker = Path.Combine(_dir, "blocker");
        await File.WriteAllTextAsync(blocker, "x", TestContext.Current.CancellationToken);
        FileBackupStore store = new(blocker);

        Assert.Null(await store.BackupAsync("/some/file.txt", "data"u8.ToArray(), TestContext.Current.CancellationToken));
    }
}
