using UiharuMind.Core.AI.Memory;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 索引库文件名的推导。
///
/// 只测不碰磁盘的那部分：路径推导是纯函数，而原子换库与改名搬迁都要真写文件，
/// 落在用户的实际数据目录下（<c>AppPaths.Data.MemoryEmbeddings</c> 是静态只读的），
/// 不适合在单测里跑——那几条留给手测清单。
///
/// 库文件名跟随记忆库名称，所以「两个不同名称推导出同一个文件名」就意味着两份知识库
/// 共用一个索引、互相覆盖。创建窗已经挡下了非法字符，这里钉住兜底行为本身。
/// </summary>
public class MemoryStoreTests
{
    /// <summary>正常名称原样用作文件名，方便在目录里跟 json 对上</summary>
    [Theory]
    [InlineData("笔记")]
    [InlineData("my-notes")]
    [InlineData("Notes 2026")]
    public void GetSafeFileName_KeepsOrdinaryNames(string name)
    {
        Assert.Equal(name, MemoryStore.GetSafeFileName(name));
    }

    /// <summary>首尾空白不进文件名,否则目录里会出现看不见的差异</summary>
    [Fact]
    public void GetSafeFileName_TrimsWhitespace()
    {
        Assert.Equal("笔记", MemoryStore.GetSafeFileName("  笔记  "));
    }

    /// <summary>全是非法字符时退回名称哈希,而不是产出空文件名</summary>
    [Fact]
    public void GetSafeFileName_FallsBackToHashWhenNothingUsable()
    {
        string safeName = MemoryStore.GetSafeFileName("/");

        Assert.False(string.IsNullOrWhiteSpace(safeName));
        Assert.DoesNotContain('/', safeName);
    }

    /// <summary>同名总是推出同一个文件名——否则重启后找不到自己上次建的索引</summary>
    [Fact]
    public void GetSafeFileName_IsDeterministic()
    {
        Assert.Equal(MemoryStore.GetSafeFileName("笔记"), MemoryStore.GetSafeFileName("笔记"));
        Assert.Equal(MemoryStore.GetSafeFileName("/"), MemoryStore.GetSafeFileName("/"));
    }

    /// <summary>不同名称推出不同路径,是「两份知识库不共用索引」的前提</summary>
    [Fact]
    public void GetDatabasePath_DiffersBetweenNames()
    {
        Assert.NotEqual(MemoryStore.GetDatabasePath("甲"), MemoryStore.GetDatabasePath("乙"));
    }

    /// <summary>库路径落在记忆缓存目录下并以 .sqlite 结尾</summary>
    [Fact]
    public void GetDatabasePath_UsesSqliteExtension()
    {
        Assert.EndsWith(".sqlite", MemoryStore.GetDatabasePath("笔记"));
    }

    /// <summary>临时库与正式库同目录——File.Replace 要求如此,跨目录换不了</summary>
    [Fact]
    public void TemporaryDatabase_SitsBesideTheRealOne()
    {
        var store = new MemoryStore(() => "笔记");

        Assert.Equal(
            Path.GetDirectoryName(store.DatabasePath),
            Path.GetDirectoryName(store.TemporaryDatabasePath));
        Assert.NotEqual(store.DatabasePath, store.TemporaryDatabasePath);
    }

    /// <summary>
    /// 路径跟着当前名称走,不在构造时定死。
    /// 定死的话改名后 store 还指着旧库,换库会写到错的地方。
    /// </summary>
    [Fact]
    public void DatabasePath_FollowsCurrentName()
    {
        string name = "甲";
        var store = new MemoryStore(() => name);
        string before = store.DatabasePath;

        name = "乙";

        Assert.NotEqual(before, store.DatabasePath);
        Assert.Equal(MemoryStore.GetDatabasePath("乙"), store.DatabasePath);
    }
}
