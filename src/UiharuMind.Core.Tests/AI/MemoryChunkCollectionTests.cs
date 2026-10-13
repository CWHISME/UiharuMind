using UiharuMind.Core.AI.Memory;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 索引库集合的读写：记录经字典落库（动态集合，AOT 可用），读回的字段要与写入一致、检索按相似度排
/// </summary>
public class MemoryChunkCollectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "uiharu-memory-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
            // sqlite 句柄偶尔晚一拍释放，残留临时目录不影响结果
        }
    }

    [Fact]
    public async Task Upsert_ThenSearch_ReturnsNearestFirst_WithEveryFieldIntact()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using MemoryChunkCollection collection =
            await MemoryChunkCollection.OpenAsync(Path.Combine(_directory, "test.sqlite"), 3, token);

        await collection.UpsertAsync(
        [
            Record("a", 0, "苹果", [1f, 0f, 0f]),
            Record("b", 7, "香蕉", [0f, 1f, 0f]),
        ], token);

        List<(MemoryChunkRecord Record, double? Score)> results = [];
        await foreach (var result in collection.SearchAsync(new float[] { 0.1f, 0.9f, 0f }, 2, token)) results.Add(result);

        Assert.Equal(["b", "a"], results.Select(x => x.Record.Id));
        MemoryChunkRecord nearest = results[0].Record;
        Assert.Equal(("香蕉", 7, "file", "src-b", "doc.md"),
            (nearest.Text, nearest.ChunkIndex, nearest.SourceKind, nearest.SourceId, nearest.SourceName));
        Assert.True(results[0].Score < results[1].Score);
    }

    private static MemoryChunkRecord Record(string id, int chunkIndex, string text, float[] embedding) => new()
    {
        Id = id,
        SourceName = "doc.md",
        SourceKind = "file",
        SourceId = "src-" + id,
        ChunkIndex = chunkIndex,
        Text = text,
        Embedding = embedding,
    };
}
