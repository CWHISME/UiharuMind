using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 文本窗靠它发现智能体改了正开着的文件：原地写、改名覆盖都得报，同目录别的文件不报
/// </summary>
public class FileChangeWatcherTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"file-watch-{Guid.NewGuid():N}");

    public FileChangeWatcherTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task InPlaceWrite_IsReported()
    {
        string path = Path.Combine(_directory, "a.txt");
        await File.WriteAllTextAsync(path, "one");
        TaskCompletionSource changed = new();
        using FileChangeWatcher watcher = new(path, () => changed.TrySetResult());

        await File.WriteAllTextAsync(path, "two");

        await changed.Task.WaitAsync(Wait);
    }

    /// <summary>先写临时文件再改名覆盖，是不少编辑工具的原子写法</summary>
    [Fact]
    public async Task ReplaceByRename_IsReported()
    {
        string path = Path.Combine(_directory, "a.txt");
        await File.WriteAllTextAsync(path, "one");
        TaskCompletionSource changed = new();
        using FileChangeWatcher watcher = new(path, () => changed.TrySetResult());

        string temp = Path.Combine(_directory, "a.txt.tmp");
        await File.WriteAllTextAsync(temp, "two");
        File.Move(temp, path, overwrite: true);

        await changed.Task.WaitAsync(Wait);
    }

    [Fact]
    public async Task OtherFileInSameDirectory_IsIgnored()
    {
        string path = Path.Combine(_directory, "a.txt");
        await File.WriteAllTextAsync(path, "one");
        TaskCompletionSource changed = new();
        using FileChangeWatcher watcher = new(path, () => changed.TrySetResult());

        await File.WriteAllTextAsync(Path.Combine(_directory, "b.txt"), "other");

        await Assert.ThrowsAsync<TimeoutException>(() => changed.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Stamp_TracksContentAndExistence()
    {
        string path = Path.Combine(_directory, "a.txt");
        Assert.False(FileStamp.Read(path).Exists);

        await File.WriteAllTextAsync(path, "one");
        FileStamp first = FileStamp.Read(path);
        Assert.True(first.Exists);
        Assert.Equal(first, FileStamp.Read(path));

        await File.WriteAllTextAsync(path, "longer");
        Assert.NotEqual(first, FileStamp.Read(path));
    }
}
