using UiharuMind.Core.Core.Clipboard;

namespace UiharuMind.Core.Tests.Clipboard;

/// <summary>
/// <see cref="ClipboardHistoryStore"/> 的落盘与检索。钉住的事实：每次变更立即落盘、
/// 置顶不篡改时间、删除把图片路径交回调用方、搜索走全量历史而不是已加载的那一页。
/// </summary>
public class ClipboardHistoryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-clip-{Guid.NewGuid():N}");

    private string DatabasePath => Path.Combine(_directory, "ClipboardHistory.db");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
            // 清理失败不该让测试变红
        }
    }

    /// <summary>
    /// 每次变更立即落盘。旧实现每小时才存一次，调试模式杀进程就丢一大截——
    /// 这里用「换一个实例再读」模拟进程被强杀后重启
    /// </summary>
    [Fact]
    public void Add_IsPersistedImmediately()
    {
        using (ClipboardHistoryStore store = new(DatabasePath))
        {
            store.AddText("第一条");
            store.AddText("第二条");
        }

        using ClipboardHistoryStore reopened = new(DatabasePath);
        List<ClipboardHistoryEntry> page = reopened.GetPage(ClipboardHistoryFilter.None, null, 10);

        Assert.Equal(2, page.Count);
        Assert.Equal("第二条", page[0].Preview); //最新的在最前
    }

    /// <summary>正文不截断，列表只带预览</summary>
    [Fact]
    public void AddText_KeepsFullTextButPreviewsFirstLine()
    {
        using ClipboardHistoryStore store = new(DatabasePath);
        string text = "首行\n" + new string('x', 10_000);

        ClipboardHistoryEntry entry = store.AddText(text);

        Assert.Equal("首行…", entry.Preview);
        Assert.Equal(text, store.GetText(entry.Id));
    }

    /// <summary>
    /// 置顶只改排序键，<b>不碰 created_at</b>——那是内容首次进剪贴板的时间，
    /// 复制一次就跳到现在是错的
    /// </summary>
    [Fact]
    public void MoveToFront_DoesNotTouchCreatedAt()
    {
        using ClipboardHistoryStore store = new(DatabasePath);
        ClipboardHistoryEntry first = store.AddText("老的");
        store.AddText("新的");

        store.MoveToFront(first.Id);
        List<ClipboardHistoryEntry> page = store.GetPage(ClipboardHistoryFilter.None, null, 10);

        Assert.Equal(first.Id, page[0].Id);
        Assert.Equal(first.CreatedAt, page[0].CreatedAt);
    }

    /// <summary>分页按排序键往下翻，不重不漏</summary>
    [Fact]
    public void GetPage_WalksWithoutGapOrOverlap()
    {
        using ClipboardHistoryStore store = new(DatabasePath);
        for (int i = 0; i < 25; i++) store.AddText($"item-{i}");

        List<ClipboardHistoryEntry> first = store.GetPage(ClipboardHistoryFilter.None, null, 10);
        List<ClipboardHistoryEntry> second = store.GetPage(ClipboardHistoryFilter.None, first[^1].SortKey, 10);
        List<ClipboardHistoryEntry> third = store.GetPage(ClipboardHistoryFilter.None, second[^1].SortKey, 10);

        Assert.Equal(10, first.Count);
        Assert.Equal(10, second.Count);
        Assert.Equal(5, third.Count); //不足一页即没有下一页
        Assert.Equal(25, first.Concat(second).Concat(third).Select(x => x.Id).Distinct().Count());
    }

    /// <summary>
    /// 搜索走的是全量历史，而不是已经加载进列表的那一页——
    /// 旧实现在内存集合上过滤，翻不到的那条也就搜不到
    /// </summary>
    [Fact]
    public void GetPage_SearchesEntireHistory()
    {
        using ClipboardHistoryStore store = new(DatabasePath);
        store.AddText("很久以前的那一条 needle");
        for (int i = 0; i < 50; i++) store.AddText($"filler-{i}");

        List<ClipboardHistoryEntry> found = store.GetPage(new ClipboardHistoryFilter("needle"), null, 10);

        Assert.Single(found);
    }

    /// <summary>关键字里的通配符要转义，否则搜 "100%" 会变成匹配任意内容</summary>
    [Fact]
    public void GetPage_EscapesWildcardsInQuery()
    {
        using ClipboardHistoryStore store = new(DatabasePath);
        store.AddText("进度 100% 完成");
        store.AddText("完全无关的一条");

        Assert.Single(store.GetPage(new ClipboardHistoryFilter("100%"), null, 10));
        Assert.Empty(store.GetPage(new ClipboardHistoryFilter("%%%"), null, 10));
    }

    /// <summary>
    /// 删除只动数据库，图片路径交回调用方——文件必须等记录落盘之后再删，
    /// 反过来崩在中间会留下指向不存在文件的记录
    /// </summary>
    [Fact]
    public void Delete_ReturnsImagePathsAndKeepsFiles()
    {
        Directory.CreateDirectory(_directory);
        string image = Path.Combine(_directory, "shot.png");
        File.WriteAllText(image, "fake");

        using ClipboardHistoryStore store = new(DatabasePath);
        ClipboardHistoryEntry entry = store.AddImage(image);

        List<string> images = store.Delete([entry.Id]);

        Assert.Equal([image], images);
        Assert.True(File.Exists(image), "删文件是调用方的事,存储层不该动它");
        Assert.Empty(store.GetPage(ClipboardHistoryFilter.None, null, 10));
    }

    /// <summary>按时间清理时收藏项豁免</summary>
    [Fact]
    public void DeleteOlderThan_KeepsFavorites()
    {
        using ClipboardHistoryStore store = new(DatabasePath);
        ClipboardHistoryEntry kept = store.AddText("收藏的");
        store.AddText("普通的");
        store.SetFavorite(kept.Id, true);

        store.DeleteOlderThan(DateTime.Now.AddMinutes(1)); //把全部记录都算作"过期"

        List<ClipboardHistoryEntry> left = store.GetPage(ClipboardHistoryFilter.None, null, 10);
        Assert.Single(left);
        Assert.Equal(kept.Id, left[0].Id);
    }

    /// <summary>孤儿图片补记一次，重复调用不会记成两条</summary>
    [Fact]
    public void AddImageIfMissing_IsIdempotent()
    {
        using ClipboardHistoryStore store = new(DatabasePath);
        string image = Path.Combine(_directory, "orphan.png");

        Assert.NotNull(store.AddImageIfMissing(image));
        Assert.Null(store.AddImageIfMissing(image));
        Assert.Equal(1, store.Count(ClipboardHistoryFilter.None));
    }
}
