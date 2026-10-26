using UiharuMind.Core.Core;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 模型目录的本地缓存：写/读/覆盖、按目录地址哈希分文件、坏文件当没缓存。
/// 语义与 McpToolCatalogs 一致：可再生、用户可随手删、离线可读（ADR 0092）。
/// </summary>
public class ModelCatalogCacheTests
{
    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "uiharu-catalog-cache-" + Guid.NewGuid().ToString("N"));

    private static ModelCatalogEntry Entry(string id) => new()
    {
        Id = id,
        Name = id,
        ContextLength = 1000,
        SupportedEndpoints = ["/chat/completions"],
    };

    [Fact]
    public void WriteThenRead_RoundTripsEntries()
    {
        var cache = new ModelCatalogCache(TempRoot());

        cache.Write("https://x/v1/models", [Entry("a"), Entry("b")]);

        IReadOnlyList<ModelCatalogEntry>? read = cache.TryRead("https://x/v1/models");
        Assert.NotNull(read);
        Assert.Equal(2, read!.Count);
        Assert.Equal("a", read[0].Id);
        Assert.Equal(1000, read[0].ContextLength);
        Assert.Equal(["/chat/completions"], read[0].SupportedEndpoints);
    }

    [Fact]
    public void WriteTwice_ReadReturnsLatest()
    {
        var cache = new ModelCatalogCache(TempRoot());

        cache.Write("https://x/v1/models", [Entry("v1")]);
        cache.Write("https://x/v1/models", [Entry("v2")]);

        Assert.Equal("v2", Assert.Single(cache.TryRead("https://x/v1/models")!).Id);
    }

    [Fact]
    public void DifferentUrl_IsDifferentCacheFile()
    {
        var cache = new ModelCatalogCache(TempRoot());

        cache.Write("https://x/v1/models", [Entry("a")]);

        Assert.Null(cache.TryRead("https://y/v1/models"));
    }

    [Fact]
    public void NoCacheFile_ReturnsNull()
    {
        var cache = new ModelCatalogCache(TempRoot());

        Assert.Null(cache.TryRead("https://x/v1/models"));
    }

    [Fact]
    public void CorruptFile_TreatedAsNoCache()
    {
        var cache = new ModelCatalogCache(TempRoot());
        string path = cache.PathFor("https://x/v1/models");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json !!");

        Assert.Null(cache.TryRead("https://x/v1/models"));
    }

    [Fact]
    public void DefaultRoot_LivesUnderModelCatalogsDirectory()
    {
        var cache = new ModelCatalogCache();

        Assert.StartsWith(AppPaths.Cache.ModelCatalogs, cache.PathFor("https://x/v1/models"));
    }
}
