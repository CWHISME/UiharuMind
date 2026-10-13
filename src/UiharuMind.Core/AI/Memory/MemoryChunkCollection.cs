using System.Runtime.CompilerServices;
using CommunityToolkit.VectorData.SqliteVec;
using Microsoft.Extensions.VectorData;

namespace UiharuMind.Core.AI.Memory;

/// <summary>
/// 索引库里的一条记录。属性名即列名(见 <see cref="MemoryChunkCollection"/>),
/// 改名或增删属性都会让既有索引对不上,必须同时抬 <see cref="MemoryData.CurrentIndexVersion"/>。
/// </summary>
internal sealed class MemoryChunkRecord
{
    public string Id { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string SourceId { get; set; } = "";
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = "";
    public ReadOnlyMemory<float> Embedding { get; set; }
}

/// <summary>
/// 一个索引库里的片段集合。底层用连接器的动态集合（按字典读写列）：强类型集合靠反射映射记录，AOT 下不可用，
/// 记录与字典的互转就收在这里一处
/// </summary>
internal sealed class MemoryChunkCollection : IDisposable
{
    /// <summary>集合名。库里只有这一个集合，改它等于让所有既有索引失效</summary>
    private const string CollectionName = "chunks";

    private readonly VectorStoreCollection<object, Dictionary<string, object?>> _collection;

    private MemoryChunkCollection(VectorStoreCollection<object, Dictionary<string, object?>> collection)
    {
        _collection = collection;
    }

    /// <summary>
    /// 打开(必要时创建)一个索引库集合
    /// </summary>
    /// <param name="databasePath">库文件路径</param>
    /// <param name="embeddingDimensions">向量维度</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>可用的集合</returns>
    public static async Task<MemoryChunkCollection> OpenAsync(
        string databasePath, int embeddingDimensions, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            {
                new VectorStoreKeyProperty(nameof(MemoryChunkRecord.Id), typeof(string)),
                new VectorStoreDataProperty(nameof(MemoryChunkRecord.SourceName), typeof(string)),
                new VectorStoreDataProperty(nameof(MemoryChunkRecord.SourceKind), typeof(string)),
                new VectorStoreDataProperty(nameof(MemoryChunkRecord.SourceId), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(MemoryChunkRecord.ChunkIndex), typeof(int)),
                new VectorStoreDataProperty(nameof(MemoryChunkRecord.Text), typeof(string)),
                new VectorStoreVectorProperty(nameof(MemoryChunkRecord.Embedding), typeof(ReadOnlyMemory<float>),
                    embeddingDimensions)
                {
                    DistanceFunction = DistanceFunction.CosineDistance,
                    IndexKind = IndexKind.Flat
                }
            }
        };

        // 索引文件会被原子替换,关闭连接池可避免下次更新复用已被移动的旧文件句柄。
        var store = new SqliteVectorStore($"Data Source={databasePath};Pooling=False", null);
        VectorStoreCollection<object, Dictionary<string, object?>> collection =
            store.GetDynamicCollection(CollectionName, definition);
        await collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
        return new MemoryChunkCollection(collection);
    }

    /// <summary>
    /// 写入一批记录
    /// </summary>
    /// <param name="records">记录</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task UpsertAsync(IEnumerable<MemoryChunkRecord> records, CancellationToken cancellationToken) =>
        _collection.UpsertAsync(records.Select(ToRow), cancellationToken);

    /// <summary>
    /// 按向量检索，结果按距离升序（即相似度降序），不带向量本身
    /// </summary>
    /// <param name="embedding">查询向量</param>
    /// <param name="top">最多返回几条</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>记录与连接器给出的分数（余弦距离，越小越像；拿不到时为 null）</returns>
    public async IAsyncEnumerable<(MemoryChunkRecord Record, double? Score)> SearchAsync(ReadOnlyMemory<float> embedding,
        int top, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        VectorSearchOptions<Dictionary<string, object?>> options = new() { IncludeVectors = false };
        await foreach (VectorSearchResult<Dictionary<string, object?>> result in _collection
                           .SearchAsync(embedding, top, options, cancellationToken).ConfigureAwait(false))
        {
            yield return (FromRow(result.Record), result.Score);
        }
    }

    public void Dispose() => _collection.Dispose();

    private static Dictionary<string, object?> ToRow(MemoryChunkRecord record) => new()
    {
        [nameof(MemoryChunkRecord.Id)] = record.Id,
        [nameof(MemoryChunkRecord.SourceName)] = record.SourceName,
        [nameof(MemoryChunkRecord.SourceKind)] = record.SourceKind,
        [nameof(MemoryChunkRecord.SourceId)] = record.SourceId,
        [nameof(MemoryChunkRecord.ChunkIndex)] = record.ChunkIndex,
        [nameof(MemoryChunkRecord.Text)] = record.Text,
        [nameof(MemoryChunkRecord.Embedding)] = record.Embedding,
    };

    private static MemoryChunkRecord FromRow(Dictionary<string, object?> row) => new()
    {
        Id = row.GetValueOrDefault(nameof(MemoryChunkRecord.Id)) as string ?? "",
        SourceName = row.GetValueOrDefault(nameof(MemoryChunkRecord.SourceName)) as string ?? "",
        SourceKind = row.GetValueOrDefault(nameof(MemoryChunkRecord.SourceKind)) as string ?? "",
        SourceId = row.GetValueOrDefault(nameof(MemoryChunkRecord.SourceId)) as string ?? "",
        ChunkIndex = row.GetValueOrDefault(nameof(MemoryChunkRecord.ChunkIndex)) is int index ? index : 0,
        Text = row.GetValueOrDefault(nameof(MemoryChunkRecord.Text)) as string ?? "",
    };
}
