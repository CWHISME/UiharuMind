using CommunityToolkit.VectorData.SqliteVec;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Memory;

/// <summary>
/// 一次索引构建的结果。状态与失败明细交回门面落盘,构建器本身不碰持久化字段。
/// </summary>
/// <param name="Status">整体结果</param>
/// <param name="Failures">逐来源的失败明细,用于本地化展示</param>
/// <param name="Error">整体失败原因,成功时为空</param>
/// <param name="SourceCount">来源总数</param>
/// <param name="ChunkCount">写入的块数</param>
internal readonly record struct MemoryIndexBuildOutcome(
    MemoryIndexUpdateStatus Status,
    IReadOnlyList<MemoryIndexSourceFailure> Failures,
    string Error,
    int SourceCount,
    int ChunkCount);

/// <summary>
/// 索引构建的编排:读来源 → 切块 → 批量嵌入 → 写临时库 → 原子换库。
///
/// 单独成类而不是留在 <see cref="MemoryData"/> 里:这是全流程里唯一又长又有状态机味道的一段
/// (五个阶段、可取消、逐来源容错、嵌入被拒还要回退重拆),和「一个记忆库的元数据」不是一件事。
/// 它不碰持久化字段——把结果交回门面,由门面决定怎么落盘。
/// </summary>
internal sealed class MemoryIndexBuilder
{
    /// <summary>
    /// 一次嵌入请求携带的块数。远程端点下这是最主要的耗时来源——逐条发意味着每块一次
    /// HTTP 往返,一份长文档就是上千次串行请求。取值偏保守:端点各有输入总量上限,
    /// 批太大会整批被拒然后退化成逐条,反而更慢。
    /// </summary>
    private const int EmbeddingBatchSize = 20;

    private readonly MemoryStore _store;

    public MemoryIndexBuilder(MemoryStore store)
    {
        _store = store;
    }

    /// <summary>
    /// 重建整个索引
    /// </summary>
    /// <param name="session">可用的嵌入会话</param>
    /// <param name="sources">要索引的来源</param>
    /// <param name="onDatabaseReplaced">库文件已被替换的回调,须在此关闭检索侧的旧句柄</param>
    /// <param name="progress">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>构建结果</returns>
    public async Task<MemoryIndexBuildOutcome> BuildAsync(
        IEmbeddingSession session,
        IReadOnlyList<MemorySourceReference> sources,
        Action onDatabaseReplaced,
        IProgress<MemoryIndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<MemoryIndexSourceFailure> failures = [];
        var reporter = new ProgressReporter(progress);

        try
        {
            _store.DeleteTemporary();
            reporter.Report(MemoryIndexStage.Preparing, 0.02, "", 0, sources.Count, 0, 0, 0);

            List<MemorySourceDocument> documents =
                await ReadSourcesAsync(sources, failures, reporter, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            reporter.Report(MemoryIndexStage.SplittingText, 0.27, "", sources.Count, sources.Count, 0, 0, 0);

            // 块预算跟着嵌入模型的上下文长度走:写死常量会让大上下文模型被白切成一堆碎块
            int chunkBudget = MemoryTextChunker.ResolveChunkBudget(
                ConfigManager.Instance.EmbeddingModelSetting.ContextSize);
            List<PendingChunk> pending = SplitDocuments(documents, chunkBudget);

            if (pending.Count == 0)
            {
                if (failures.Count > 0) return Fail("Memory source validation failed", failures, sources.Count);

                _store.ReplaceWithEmpty();
                onDatabaseReplaced();
                reporter.Report(MemoryIndexStage.Completed, 1, "", sources.Count, sources.Count, 0, 0, 0);
                return new MemoryIndexBuildOutcome(
                    MemoryIndexUpdateStatus.Succeeded, failures, "", sources.Count, 0);
            }

            List<MemoryChunkRecord> records = await EmbedAsync(
                session, pending, sources.Count, failures.Count, reporter, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (failures.Count > 0) return Fail("Memory source validation failed", failures, sources.Count);

            reporter.Report(MemoryIndexStage.WritingDatabase, 0.92, "", sources.Count, sources.Count,
                records.Count, records.Count, failures.Count);
            await WriteTemporaryAsync(records, cancellationToken).ConfigureAwait(false);

            // 临时库完整写入后才替换正式库,取消或失败不会污染上一次成功索引。
            _store.ReplaceWithTemporary();
            onDatabaseReplaced();
            reporter.Report(MemoryIndexStage.Completed, 1, "", sources.Count, sources.Count,
                records.Count, records.Count, 0);
            return new MemoryIndexBuildOutcome(
                MemoryIndexUpdateStatus.Succeeded, failures, "", sources.Count, records.Count);
        }
        catch (OperationCanceledException)
        {
            _store.DeleteTemporary();
            return new MemoryIndexBuildOutcome(
                MemoryIndexUpdateStatus.Cancelled, failures, "", sources.Count, 0);
        }
        catch (EmbeddingInputTooLargeException)
        {
            _store.DeleteTemporary();
            return Fail("Embedding input is too large", failures, sources.Count);
        }
        catch (Exception e)
        {
            _store.DeleteTemporary();
            Log.Error(e.Message);
            return Fail(e.Message, failures, sources.Count);
        }
    }

    private static MemoryIndexBuildOutcome Fail(
        string error, IReadOnlyList<MemoryIndexSourceFailure> failures, int sourceCount)
    {
        return new MemoryIndexBuildOutcome(MemoryIndexUpdateStatus.Failed, failures, error, sourceCount, 0);
    }

    private async Task WriteTemporaryAsync(
        List<MemoryChunkRecord> records, CancellationToken cancellationToken)
    {
        // 维度取自实际拿到的向量,而不是配置里声称的:两者不一致时以库里真实存的为准
        SqliteCollection<string, MemoryChunkRecord> collection = await MemoryStore.CreateCollectionAsync(
            _store.TemporaryDatabasePath, records[0].Embedding.Length, cancellationToken).ConfigureAwait(false);
        try
        {
            await collection.UpsertAsync(records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            collection.Dispose();
        }
    }

    private static async Task<List<MemorySourceDocument>> ReadSourcesAsync(
        IReadOnlyList<MemorySourceReference> sources,
        List<MemoryIndexSourceFailure> failures,
        ProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        List<MemorySourceDocument> documents = [];
        for (int index = 0; index < sources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MemorySourceReference source = sources[index];
            reporter.Report(MemoryIndexStage.ReadingSources,
                0.05 + 0.20 * index / Math.Max(1, sources.Count),
                source.DisplayName, index, sources.Count, 0, 0, failures.Count);

            MemorySourceReadResult readResult =
                await MemorySourceReaders.ReadAsync(source, cancellationToken).ConfigureAwait(false);

            if (!readResult.Success || readResult.Document == null)
            {
                failures.Add(new MemoryIndexSourceFailure(
                    source.DisplayName, readResult.ErrorCode, readResult.ErrorDetail));
            }
            else
            {
                documents.Add(readResult.Document);
            }

            reporter.Report(MemoryIndexStage.ReadingSources,
                0.05 + 0.20 * (index + 1) / Math.Max(1, sources.Count),
                source.DisplayName, index + 1, sources.Count, 0, 0, failures.Count);
        }

        return documents;
    }

    private static List<PendingChunk> SplitDocuments(List<MemorySourceDocument> documents, int chunkBudget)
    {
        List<PendingChunk> pending = [];
        foreach (MemorySourceDocument document in documents)
        {
            IEnumerable<MemoryChunk> chunks = MemoryTextChunker.Split(
                document.Text, document.SourceName, chunkBudget);
            foreach (MemoryChunk chunk in chunks)
            {
                pending.Add(new PendingChunk(document, chunk.EmbeddingText));
            }
        }

        return pending;
    }

    /// <summary>
    /// 逐批嵌入。整批被端点拒收时退成逐条——批量请求只会说「输入太大」,
    /// 不会说是哪一条,只有单条被拒才知道该拆谁。
    /// </summary>
    private static async Task<List<MemoryChunkRecord>> EmbedAsync(
        IEmbeddingSession session,
        List<PendingChunk> pending,
        int sourceCount,
        int failureCount,
        ProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        List<MemoryChunkRecord> records = [];
        Dictionary<string, int> sourceChunkIndices = [];
        int cursor = 0;

        while (cursor < pending.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int batchSize = Math.Min(EmbeddingBatchSize, pending.Count - cursor);
            reporter.Report(MemoryIndexStage.GeneratingEmbeddings,
                0.30 + 0.60 * cursor / pending.Count,
                pending[cursor].Document.SourceName, sourceCount, sourceCount,
                cursor, pending.Count, failureCount);

            IReadOnlyList<ReadOnlyMemory<float>>? embeddings =
                await TryEmbedAsync(session, pending, cursor, batchSize, cancellationToken).ConfigureAwait(false);

            if (embeddings == null && batchSize > 1)
            {
                batchSize = 1;
                embeddings = await TryEmbedAsync(session, pending, cursor, 1, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (embeddings == null)
            {
                // 单条仍被拒:tokenizer 因模型而异,按 token 预算切好也可能超限,
                // 所以按实际拒收结果做二次拆分,而不是一开始就把块切得很碎。
                string text = pending[cursor].Text;
                if (!MemoryTextChunker.CanSplitFurther(text))
                    throw new EmbeddingInputTooLargeException("Embedding input is too large");

                (string first, string second) = MemoryTextChunker.SplitOversized(text);
                PendingChunk original = pending[cursor];
                pending[cursor] = original with { Text = first };
                pending.Insert(cursor + 1, original with { Text = second });
                continue;
            }

            for (int offset = 0; offset < batchSize; offset++)
            {
                PendingChunk chunk = pending[cursor + offset];
                int chunkIndex = sourceChunkIndices.GetValueOrDefault(chunk.Document.SourceId);
                sourceChunkIndices[chunk.Document.SourceId] = chunkIndex + 1;
                records.Add(new MemoryChunkRecord
                {
                    Id = $"{chunk.Document.SourceId}_{chunkIndex}",
                    SourceName = chunk.Document.SourceName,
                    SourceKind = chunk.Document.SourceKind,
                    SourceId = chunk.Document.SourceId,
                    ChunkIndex = chunkIndex,
                    Text = chunk.Text,
                    Embedding = embeddings[offset]
                });
            }

            cursor += batchSize;
        }

        return records;
    }

    /// <summary>
    /// 嵌入一批文本
    /// </summary>
    /// <returns>向量;整批被判定输入过长时返回 null,其余异常照常抛出</returns>
    private static async Task<IReadOnlyList<ReadOnlyMemory<float>>?> TryEmbedAsync(
        IEmbeddingSession session,
        List<PendingChunk> pending,
        int cursor,
        int count,
        CancellationToken cancellationToken)
    {
        string[] texts = new string[count];
        for (int offset = 0; offset < count; offset++) texts[offset] = pending[cursor + offset].Text;

        try
        {
            return await session.GenerateEmbeddingsAsync(texts, cancellationToken).ConfigureAwait(false);
        }
        catch (EmbeddingInputTooLargeException)
        {
            return null;
        }
    }

    private sealed record PendingChunk(MemorySourceDocument Document, string Text);

    /// <summary>
    /// 进度上报。百分比只准涨不准跌——超长块被拒后会拆成两块,分母因此变大,
    /// 照实算会让进度条往回跳,而用户只会以为卡住重来了。
    /// </summary>
    private sealed class ProgressReporter
    {
        private readonly IProgress<MemoryIndexProgress>? _progress;
        private double _highWaterMark;

        public ProgressReporter(IProgress<MemoryIndexProgress>? progress)
        {
            _progress = progress;
        }

        public void Report(
            MemoryIndexStage stage, double percentage, string source,
            int processedSources, int totalSources, int currentChunk, int totalChunks, int failedSources)
        {
            if (_progress == null) return;

            _highWaterMark = Math.Max(_highWaterMark, Math.Clamp(percentage, 0, 1));
            _progress.Report(new MemoryIndexProgress(stage, _highWaterMark, source,
                processedSources, totalSources, currentChunk, totalChunks, failedSources));
        }
    }
}
