using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 路径最近记录的公共实现：置顶、去重、裁尾、剔除失效、清空。
/// 工作区历史、搜索目录历史、文本文件历史都委托给它。
/// </summary>
public class RecentPathListTests
{
    private static string TempFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"recent-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Remember_PutsLatestFirst()
    {
        var storage = new List<string>();
        var recent = new RecentPathList(storage, 10);
        string first = TempFile();
        string second = TempFile();
        try
        {
            Assert.True(recent.Remember(first));
            Assert.True(recent.Remember(second));

            Assert.Equal([Path.GetFullPath(second), Path.GetFullPath(first)], recent.Items);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void Remember_DeduplicatesAndMovesToTop()
    {
        var storage = new List<string>();
        var recent = new RecentPathList(storage, 10);
        string first = TempFile();
        string second = TempFile();
        try
        {
            recent.Remember(first);
            recent.Remember(second);

            Assert.True(recent.Remember(first));

            Assert.Equal(2, recent.Items.Count);
            Assert.Equal(Path.GetFullPath(first), recent.Items[0]);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void Remember_TrimsBeyondLimit()
    {
        var storage = new List<string>();
        var recent = new RecentPathList(storage, 2);
        string first = TempFile();
        string second = TempFile();
        string third = TempFile();
        try
        {
            recent.Remember(first);
            recent.Remember(second);
            recent.Remember(third);

            Assert.Equal(2, recent.Items.Count);
            Assert.DoesNotContain(Path.GetFullPath(first), recent.Items);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
            File.Delete(third);
        }
    }

    [Fact]
    public void Remember_IgnoresEmptyAndReturnsFalseWhenUnchanged()
    {
        var storage = new List<string>();
        var recent = new RecentPathList(storage, 10);

        Assert.False(recent.Remember(null));
        Assert.False(recent.Remember("   "));
        Assert.Empty(recent.Items);
    }

    [Fact]
    public void Forget_RemovesEntry()
    {
        var storage = new List<string>();
        var recent = new RecentPathList(storage, 10);
        string path = TempFile();
        try
        {
            recent.Remember(path);

            Assert.True(recent.Forget(path));
            Assert.Empty(recent.Items);
            Assert.False(recent.Forget(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PruneMissing_RemovesDeletedPaths()
    {
        string alive = TempFile();
        string gone = Path.Combine(Path.GetTempPath(), $"recent-{Guid.NewGuid():N}.txt");
        var storage = new List<string> { alive, gone };
        var recent = new RecentPathList(storage, 10);
        try
        {
            Assert.True(recent.PruneMissing());
            Assert.Equal([alive], recent.Items);
            Assert.False(recent.PruneMissing());
        }
        finally
        {
            File.Delete(alive);
        }
    }

    [Fact]
    public void Clear_EmptiesList()
    {
        string path = TempFile();
        var storage = new List<string> { path };
        var recent = new RecentPathList(storage, 10);
        try
        {
            Assert.True(recent.Clear());
            Assert.Empty(recent.Items);
            Assert.False(recent.Clear());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
