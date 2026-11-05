using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 单层枚举核心：额度按过滤后的条数算（被 include 滤掉的不占名额、也不顶出 HasMore）、
/// 没额度按「还有更多」算、目录优先收集（目录名排在文件后面也不会被挤掉）。
/// 这些语义从前只在 Read 的文本里间接体现，一处走样就没人接得住
/// </summary>
public sealed class DirectoryListerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-dirlist-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言
        }
    }

    private void CreateFile(string relative)
    {
        string full = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, []);
    }

    private void MakeDir(string relative) => Directory.CreateDirectory(Path.Combine(_dir, relative));

    [Fact]
    public void List_EmptyDirectory_ReturnsNothing()
    {
        DirectoryList result = DirectoryLister.List(_dir, 10, CancellationToken.None);

        Assert.Empty(result.Entries);
        Assert.False(result.HasMore);
    }

    /// <summary>条目数正好等于上限时就是全部，不能说「还有更多」</summary>
    [Fact]
    public void List_ExactlyAtTheLimit_IsNotTruncated()
    {
        CreateFile("f0.txt");
        CreateFile("f1.txt");
        CreateFile("f2.txt");

        DirectoryList result = DirectoryLister.List(_dir, 3, CancellationToken.None);

        Assert.Equal(3, result.Entries.Count);
        Assert.False(result.HasMore);
    }

    [Fact]
    public void List_OnePastTheLimit_ReportsMore()
    {
        for (int i = 0; i < 4; i++) CreateFile($"f{i}.txt");

        DirectoryList result = DirectoryLister.List(_dir, 3, CancellationToken.None);

        Assert.Equal(3, result.Entries.Count);
        Assert.True(result.HasMore);
    }

    /// <summary>
    /// 额度按过滤后的条数算：被 include 滤掉的既不占名额，也不把 HasMore 顶成误报。
    /// 判定若在过滤之前，只收目录时「目录恰好收满、后面只剩文件」就会假报「还有更多」
    /// </summary>
    [Fact]
    public void List_IncludeDoesNotSpendTheQuota()
    {
        MakeDir("d0");
        MakeDir("d1");
        CreateFile("a.txt");
        CreateFile("b.txt");

        DirectoryList directories = DirectoryLister.List(_dir, 2, CancellationToken.None, include: e => e.IsDirectory);
        DirectoryList files = DirectoryLister.List(_dir, 2, CancellationToken.None, include: e => !e.IsDirectory);

        Assert.Equal(2, directories.Entries.Count);
        Assert.False(directories.HasMore);
        Assert.Equal(2, files.Entries.Count);
        Assert.False(files.HasMore);
    }

    [Fact]
    public void List_NoBudget_ReportsMore()
    {
        CreateFile("a.txt");

        DirectoryList result = DirectoryLister.List(_dir, 0, CancellationToken.None);

        Assert.Empty(result.Entries);
        Assert.True(result.HasMore); // 一条都没看，不能说「没有了」
    }

    /// <summary>目录优先：文件多到超过额度、目录名又排在最后，目录仍要收进来，文件让位</summary>
    [Fact]
    public void ListDirectoriesFirst_KeepsDirectoriesWhenFilesFillTheQuota()
    {
        for (int i = 0; i < 5; i++) CreateFile($"a{i}.txt");
        MakeDir("zdir");

        DirectoryFirstListing result = DirectoryLister.ListDirectoriesFirst(_dir, 3, CancellationToken.None);

        Assert.Equal("zdir", Assert.Single(result.Directories).Name);
        Assert.Equal(2, result.Files.Count); // 额度 3 - 1 个目录
        Assert.True(result.HasMoreFiles);
        Assert.False(result.HasMoreDirectories);
    }

    /// <summary>目录恰好占满额度、目录里没有文件：不能说「还有文件」</summary>
    [Fact]
    public void ListDirectoriesFirst_DirectoriesFullWithoutFiles_SaysNoMoreFiles()
    {
        for (int i = 0; i < 3; i++) MakeDir($"d{i}");

        DirectoryFirstListing result = DirectoryLister.ListDirectoriesFirst(_dir, 3, CancellationToken.None);

        Assert.Equal(3, result.Directories.Count);
        Assert.False(result.HasMoreDirectories);
        Assert.False(result.HasMoreFiles);
    }

    /// <summary>目录占满额度、文件确实被跳过了：要如实标出来（不能靠「目录池满」反推）</summary>
    [Fact]
    public void ListDirectoriesFirst_DirectoriesFullWithFiles_MarksFilesAsSkipped()
    {
        CreateFile("aaa.txt"); // 名字排在目录之前，遍历时会被看到
        for (int i = 0; i < 3; i++) MakeDir($"d{i}");

        DirectoryFirstListing result = DirectoryLister.ListDirectoriesFirst(_dir, 3, CancellationToken.None);

        Assert.Empty(result.Files);
        Assert.False(result.HasMoreDirectories);
        Assert.True(result.HasMoreFiles);
    }

    /// <summary>子目录超过额度：多出来的那些要标出来</summary>
    [Fact]
    public void ListDirectoriesFirst_MoreDirectoriesThanTheQuota_ReportsMore()
    {
        for (int i = 0; i < 4; i++) MakeDir($"d{i}");

        DirectoryFirstListing result = DirectoryLister.ListDirectoriesFirst(_dir, 3, CancellationToken.None);

        Assert.Equal(3, result.Directories.Count);
        Assert.True(result.HasMoreDirectories);
    }

    [Fact]
    public void ListDirectoriesFirst_NoBudget_ReturnsNothing()
    {
        CreateFile("a.txt");
        MakeDir("d0");

        DirectoryFirstListing result = DirectoryLister.ListDirectoriesFirst(_dir, 0, CancellationToken.None);

        Assert.Empty(result.Directories);
        Assert.Empty(result.Files);
        Assert.True(result.HasMoreDirectories);
        Assert.True(result.HasMoreFiles);
    }
}
