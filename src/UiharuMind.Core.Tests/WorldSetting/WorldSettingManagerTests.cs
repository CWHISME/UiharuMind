using UiharuMind.Core.AI.WorldSettings;

namespace UiharuMind.Core.Tests.WorldSettings;

/// <summary>
/// 世界书管理器的共享语义：不存在时新建（名字原样）、已存在时按 (Keys+Content) 去重追加、按名取书。
/// </summary>
public class WorldSettingManagerTests
{
    [Fact]
    public void Merge_CreatesBook_WhenMissing()
    {
        WorldSettingManager manager = WorldSettingManager.Instance;

        WorldSetting book = manager.Merge("测试书", [new WorldSettingEntry { Content = "A", Keys = ["k"] }]);

        Assert.Equal("测试书", book.Name);
        Assert.Same(book, manager.Get("测试书"));
        Assert.Single(book.Entries);
    }

    [Fact]
    public void Merge_Appends_WithDedupe()
    {
        WorldSettingManager manager = WorldSettingManager.Instance;
        manager.Merge("去重书", [new WorldSettingEntry { Content = "A", Keys = ["k"] }]);

        WorldSetting book = manager.Merge("去重书",
        [
            new WorldSettingEntry { Content = "A", Keys = ["k"] },
            new WorldSettingEntry { Content = "B", Keys = ["k2"] },
        ]);

        Assert.Equal(2, book.Entries.Count);
        Assert.Equal("B", book.Entries[^1].Content);
    }

    [Fact]
    public void Get_MissingOrEmpty_ReturnsNull()
    {
        WorldSettingManager manager = WorldSettingManager.Instance;

        Assert.Null(manager.Get(""));
        Assert.Null(manager.Get("不存在的书"));
    }
}