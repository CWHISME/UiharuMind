using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Embeded;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Memery;

public class MemoryData : IUniquieContainerItem
{
    private const int ChunkMaxLength = 900;
    private const int ChunkOverlap = 120;
    private const int MinimumAdaptiveChunkLength = 48;
    private const string CollectionName = "chunks";
    private static readonly TimeSpan EmbeddingServerStartTimeout = TimeSpan.FromMinutes(2);
    private static readonly IMemorySourceReader[] SourceReaders =
    [
        new ManualTextSourceReader(),
        new PlainTextFileSourceReader()
    ];

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<MemoryTextSource> TextSources { get; set; } = [];
    public List<string> FilePaths { get; set; } = [];
    public bool IndexDirty { get; set; }
    public DateTime? LastIndexedAt { get; set; }
    public string LastIndexError { get; set; } = "";

    public event Action? StateChanged;

    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private UiharaTextEmbeddingGenerator? _embeddingGenerator;
    private SqliteCollection<string, MemoryChunkRecord>? _collection;
    private int? _embeddingDimensions;
    private bool _isVectorStoreUnavailable;

    public async Task<string> GetLongTermMemory(string query, bool asChunks = true)
    {
        try
        {
            if (!await EnsureReadyForSearchAsync().ConfigureAwait(false)) return "";
            if (_embeddingGenerator == null || string.IsNullOrWhiteSpace(query) || _isVectorStoreUnavailable) return "";

            ReadOnlyMemory<float> queryEmbedding =
                await GenerateSearchEmbeddingAsync(query).ConfigureAwait(false);
            SqliteCollection<string, MemoryChunkRecord>? collection =
                await EnsureSearchCollectionAsync(queryEmbedding.Length).ConfigureAwait(false);
            if (collection == null) return "";

            List<VectorSearchResult<MemoryChunkRecord>> memories = [];
            await foreach (VectorSearchResult<MemoryChunkRecord> result in collection.SearchAsync(queryEmbedding, 10,
                               new VectorSearchOptions<MemoryChunkRecord> { IncludeVectors = false }))
            {
                memories.Add(result);
            }

            if (memories.Count == 0) return "";

            StringBuilder sb = StringBuilderPool.Get();
            for (int i = 0; i < memories.Count; i++)
            {
                VectorSearchResult<MemoryChunkRecord> result = memories[i];
                sb.AppendLine("SourceName: " + result.Record.SourceName);
                sb.AppendLine("PartitionId: " + result.Record.ChunkIndex);
                sb.AppendLine("Relevance: " + result.Score);
                sb.AppendLine("Content: " + result.Record.Text);
                if (i < memories.Count - 1) sb.AppendLine("\n***\n");
            }

            string text = sb.ToString();
            StringBuilderPool.Release(sb);
            return text;
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
            return "";
        }
    }

    public async Task<MemoryIndexUpdateResult> UpdateIndexAsync(
        IProgress<MemoryIndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string temporaryDatabasePath = GetTemporaryDatabasePath();
        List<MemoryIndexSourceFailure> failures = [];
        SqliteCollection<string, MemoryChunkRecord>? temporaryCollection = null;
        bool lockAcquired = false;

        try
        {
            await _indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockAcquired = true;
            DeleteDatabaseFiles(temporaryDatabasePath);
            Report(progress, MemoryIndexStage.Preparing, 0.02, "", 0, SourceCount, 0, 0, 0);
            if (!await EnsureReadyForSearchAsync(cancellationToken).ConfigureAwait(false) ||
                _embeddingGenerator == null)
            {
                return FailUpdate(LastIndexError, failures);
            }

            cancellationToken.ThrowIfCancellationRequested();
            List<MemorySourceDocument> documents = [];
            List<MemorySourceReference> sources = BuildSourceReferences();
            for (int index = 0; index < sources.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MemorySourceReference source = sources[index];
                Report(progress, MemoryIndexStage.ReadingSources,
                    0.05 + 0.20 * index / Math.Max(1, sources.Count),
                    source.DisplayName, index, sources.Count, 0, 0, failures.Count);

                IMemorySourceReader? reader = SourceReaders.FirstOrDefault(x => x.CanRead(source));
                MemorySourceReadResult readResult = reader == null
                    ? new MemorySourceReadResult(false, ErrorCode: "MemorySourceUnsupported")
                    : await reader.ReadAsync(source, cancellationToken).ConfigureAwait(false);

                if (!readResult.Success || readResult.Document == null)
                {
                    failures.Add(new MemoryIndexSourceFailure(
                        source.DisplayName, readResult.ErrorCode, readResult.ErrorDetail));
                }
                else
                {
                    documents.Add(readResult.Document);
                }

                Report(progress, MemoryIndexStage.ReadingSources,
                    0.05 + 0.20 * (index + 1) / Math.Max(1, sources.Count),
                    source.DisplayName, index + 1, sources.Count, 0, 0, failures.Count);
            }

            Report(progress, MemoryIndexStage.SplittingText, 0.27, "", sources.Count, sources.Count, 0, 0, 0);
            List<PendingChunk> pendingChunks = [];
            foreach (MemorySourceDocument document in documents)
            {
                foreach (string chunk in SplitText(document.Text))
                {
                    pendingChunks.Add(new PendingChunk(document, chunk));
                }
            }

            if (pendingChunks.Count == 0)
            {
                if (failures.Count > 0)
                    return FailUpdate("Memory source validation failed", failures);

                ReplaceDatabaseWithEmptyIndex(temporaryDatabasePath);
                CompleteUpdate(progress, sources.Count, 0);
                return new MemoryIndexUpdateResult(MemoryIndexUpdateStatus.Succeeded, failures);
            }

            List<MemoryChunkRecord> records = [];
            Dictionary<string, int> sourceChunkIndices = [];
            int chunkCursor = 0;
            while (chunkCursor < pendingChunks.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PendingChunk pending = pendingChunks[chunkCursor];
                Report(progress, MemoryIndexStage.GeneratingEmbeddings,
                    0.30 + 0.60 * chunkCursor / pendingChunks.Count,
                    pending.Document.SourceName, sources.Count, sources.Count,
                    chunkCursor, pendingChunks.Count, failures.Count);

                ReadOnlyMemory<float> embedding;
                try
                {
                    embedding = await _embeddingGenerator
                        .GenerateEmbeddingAsync(pending.Text, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (EmbeddingInputTooLargeException) when (pending.Text.Length > MinimumAdaptiveChunkLength)
                {
                    // tokenizer 因模型而异；服务拒绝输入时只拆分当前块，并继续原索引任务。
                    (string first, string second) = SplitOversizedChunk(pending.Text);
                    pendingChunks[chunkCursor] = new PendingChunk(pending.Document, first);
                    pendingChunks.Insert(chunkCursor + 1, new PendingChunk(pending.Document, second));
                    continue;
                }

                temporaryCollection ??= await CreateCollectionAsync(
                    temporaryDatabasePath, embedding.Length, cancellationToken).ConfigureAwait(false);

                int chunkIndex = sourceChunkIndices.GetValueOrDefault(pending.Document.SourceId);
                sourceChunkIndices[pending.Document.SourceId] = chunkIndex + 1;
                records.Add(new MemoryChunkRecord
                {
                    Id = $"{pending.Document.SourceId}_{chunkIndex}",
                    SourceName = pending.Document.SourceName,
                    SourceKind = pending.Document.SourceKind,
                    SourceId = pending.Document.SourceId,
                    ChunkIndex = chunkIndex,
                    Text = pending.Text,
                    Embedding = embedding
                });
                chunkCursor++;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, MemoryIndexStage.WritingDatabase, 0.92, "", sources.Count, sources.Count,
                pendingChunks.Count, pendingChunks.Count, failures.Count);
            await temporaryCollection!.UpsertAsync(records, cancellationToken).ConfigureAwait(false);
            temporaryCollection.Dispose();
            temporaryCollection = null;

            if (failures.Count > 0)
            {
                DeleteDatabaseFiles(temporaryDatabasePath);
                return FailUpdate("Memory source validation failed", failures);
            }

            // 临时库完整写入后才替换正式库，取消或失败不会污染上一次成功索引。
            ReplaceDatabase(temporaryDatabasePath);
            CompleteUpdate(progress, sources.Count, pendingChunks.Count);
            return new MemoryIndexUpdateResult(MemoryIndexUpdateStatus.Succeeded, failures);
        }
        catch (OperationCanceledException)
        {
            DeleteDatabaseFiles(temporaryDatabasePath);
            IndexDirty = true;
            SaveIndexState();
            return new MemoryIndexUpdateResult(MemoryIndexUpdateStatus.Cancelled, failures);
        }
        catch (EmbeddingInputTooLargeException)
        {
            DeleteDatabaseFiles(temporaryDatabasePath);
            return FailUpdate("Embedding input is too large", failures);
        }
        catch (Exception e)
        {
            DeleteDatabaseFiles(temporaryDatabasePath);
            Log.Error(e.Message);
            return FailUpdate(e.Message, failures);
        }
        finally
        {
            temporaryCollection?.Dispose();
            if (lockAcquired) _indexLock.Release();
        }
    }

    public async Task<MemorySourceReadResult> ValidateTextFileAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        var source = new MemorySourceReference(
            GetSourceId(filePath), Path.GetFileName(filePath), MemorySourceKind.PlainTextFile, filePath);
        return await SourceReaders.OfType<PlainTextFileSourceReader>().Single()
            .ReadAsync(source, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> EnsureReadyForSearchAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(Name))
        {
            LastIndexError = "Memory name not set";
            Log.Error(LastIndexError);
            return false;
        }

        return await WrapTryEnsureEmbeddedServerAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Save()
    {
        IndexDirty = true;
        LastIndexError = "";
        SaveIndexState();
    }

    public void SaveMetadata()
    {
        SaveIndexState();
    }

    public void DeleteStoredIndex()
    {
        ResetSearchCollection();
        DeleteDatabaseFiles(GetDatabasePath());
        DeleteDatabaseFiles(GetTemporaryDatabasePath());
        DeleteDatabaseFiles(GetDatabasePath() + ".backup");
    }

    private int SourceCount => TextSources.Count + FilePaths.Count;

    private List<MemorySourceReference> BuildSourceReferences()
    {
        List<MemorySourceReference> sources = [];
        sources.AddRange(TextSources.Select(source => new MemorySourceReference(
            source.Id, source.Title, MemorySourceKind.ManualText, Content: source.Content)));
        sources.AddRange(FilePaths.Select(path => new MemorySourceReference(
            GetSourceId(path), Path.GetFileName(path), MemorySourceKind.PlainTextFile, path)));
        return sources;
    }

    private MemoryIndexUpdateResult FailUpdate(
        string error, IReadOnlyList<MemoryIndexSourceFailure> failures)
    {
        IndexDirty = true;
        LastIndexError = string.IsNullOrWhiteSpace(error) ? "Memory index update failed" : error;
        SaveIndexState();
        return new MemoryIndexUpdateResult(MemoryIndexUpdateStatus.Failed, failures, LastIndexError);
    }

    private void CompleteUpdate(
        IProgress<MemoryIndexProgress>? progress, int sourceCount, int chunkCount)
    {
        IndexDirty = false;
        LastIndexError = "";
        LastIndexedAt = DateTime.UtcNow;
        SaveIndexState();
        Report(progress, MemoryIndexStage.Completed, 1, "", sourceCount, sourceCount,
            chunkCount, chunkCount, 0);
    }

    private void ReplaceDatabaseWithEmptyIndex(string temporaryDatabasePath)
    {
        DeleteDatabaseFiles(temporaryDatabasePath);
        ResetSearchCollection();
        MoveDatabaseAsideAndDelete(GetDatabasePath());
    }

    private void ReplaceDatabase(string temporaryDatabasePath)
    {
        string databasePath = GetDatabasePath();
        string backupPath = databasePath + ".backup";
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        DeleteDatabaseFiles(backupPath);

        // 正式库和临时库位于同一目录，File.Replace 会以原子方式切换，并留下可回滚备份。
        if (File.Exists(databasePath))
        {
            File.Replace(temporaryDatabasePath, databasePath, backupPath, true);
            try
            {
                DeleteDatabaseFiles(backupPath);
            }
            catch (Exception e)
            {
                // 新索引已经原子生效，备份清理失败只记录日志，不能把成功更新误报为失败。
                Log.Warning($"Memory index backup cleanup failed: {backupPath}, {e.Message}");
            }
        }
        else
        {
            File.Move(temporaryDatabasePath, databasePath);
        }

        ResetSearchCollection();
    }

    private static void MoveDatabaseAsideAndDelete(string databasePath)
    {
        string backupPath = databasePath + ".backup";
        DeleteDatabaseFiles(backupPath);
        if (!File.Exists(databasePath)) return;

        File.Move(databasePath, backupPath);
        try
        {
            DeleteDatabaseFiles(backupPath);
        }
        catch
        {
            File.Move(backupPath, databasePath, true);
            throw;
        }
    }

    private void ResetSearchCollection()
    {
        _collection?.Dispose();
        _collection = null;
        _embeddingDimensions = null;
        _isVectorStoreUnavailable = false;
    }

    private void SaveIndexState()
    {
        MemoryManager.Instance.Save(this);
        StateChanged?.Invoke();
    }

    private async Task<SqliteCollection<string, MemoryChunkRecord>?> EnsureSearchCollectionAsync(
        int embeddingDimensions)
    {
        if (_isVectorStoreUnavailable) return null;
        if (_embeddingDimensions != null && _embeddingDimensions != embeddingDimensions)
        {
            LastIndexError = "Memory vector dimension mismatch";
            _isVectorStoreUnavailable = true;
            StateChanged?.Invoke();
            return null;
        }

        if (_collection != null) return _collection;
        try
        {
            _embeddingDimensions = embeddingDimensions;
            _collection = await CreateCollectionAsync(
                GetDatabasePath(), embeddingDimensions, CancellationToken.None).ConfigureAwait(false);
            return _collection;
        }
        catch (Exception e)
        {
            _isVectorStoreUnavailable = true;
            LastIndexError = e.Message;
            Log.Warning($"Memory vector store unavailable: {Name}, {e.Message}");
            StateChanged?.Invoke();
            return null;
        }
    }

    private static async Task<SqliteCollection<string, MemoryChunkRecord>> CreateCollectionAsync(
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

        // 索引文件会被原子替换，关闭连接池可避免下次更新复用已被移动的旧文件句柄。
        var store = new SqliteVectorStore($"Data Source={databasePath};Pooling=False", null);
        SqliteCollection<string, MemoryChunkRecord> collection =
            store.GetCollection<string, MemoryChunkRecord>(CollectionName, definition);
        await collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
        return collection;
    }

    private async Task<bool> WrapTryEnsureEmbeddedServerAsync(CancellationToken cancellationToken)
    {
        if (_embeddingGenerator != null) return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        LlmManager.Instance.RuntimeEngineManager.TryEnsureEmbededServer(config =>
        {
            if (config == null)
            {
                LastIndexError = "Embedding server is unavailable.";
                tcs.TrySetResult(false);
                return;
            }

            _embeddingGenerator =
                new UiharaTextEmbeddingGenerator(config.Endpoint, config.EmbeddingModelMaxTokenTotal);
            tcs.TrySetResult(true);
        });

        Task timeoutTask = Task.Delay(EmbeddingServerStartTimeout, cancellationToken);
        Task completedTask = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (completedTask == timeoutTask)
        {
            LastIndexError = "Embedding server startup timed out.";
            return false;
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    private static IEnumerable<string> SplitText(string text)
    {
        string normalized = text.Replace("\r\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(normalized)) yield break;

        int start = 0;
        while (start < normalized.Length)
        {
            int length = Math.Min(ChunkMaxLength, normalized.Length - start);
            yield return normalized.Substring(start, length);
            if (start + length >= normalized.Length) break;
            start += Math.Max(1, ChunkMaxLength - ChunkOverlap);
        }
    }

    private async Task<ReadOnlyMemory<float>> GenerateSearchEmbeddingAsync(string query)
    {
        if (_embeddingGenerator == null) throw new InvalidOperationException("Embedding server is unavailable.");

        string candidate = query.Trim();
        while (true)
        {
            try
            {
                return await _embeddingGenerator.GenerateEmbeddingAsync(candidate).ConfigureAwait(false);
            }
            catch (EmbeddingInputTooLargeException) when (candidate.Length > MinimumAdaptiveChunkLength)
            {
                // 检索只需要表达当前意图，过长时保留前半段，避免一次聊天查询拖垮记忆检索。
                candidate = candidate[..Math.Max(MinimumAdaptiveChunkLength, candidate.Length / 2)].Trim();
            }
        }
    }

    private static (string First, string Second) SplitOversizedChunk(string text)
    {
        int middle = text.Length / 2;
        int split = FindNearbySplit(text, middle);
        string first = text[..split].Trim();
        string second = text[split..].Trim();
        if (first.Length == 0 || second.Length == 0)
        {
            first = text[..middle];
            second = text[middle..];
        }

        return (first, second);
    }

    private static int FindNearbySplit(string text, int middle)
    {
        for (int offset = 0; offset < Math.Min(160, middle); offset++)
        {
            int after = middle + offset;
            if (after < text.Length && char.IsWhiteSpace(text[after])) return after;

            int before = middle - offset;
            if (before > 0 && char.IsWhiteSpace(text[before])) return before;
        }

        return middle;
    }

    private static string GetSourceId(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToUpperInvariant();
    }

    private string GetDatabasePath()
    {
        return Path.Combine(SettingConfig.MemoryEmbededPath, $"{GetSafeFileName(Name)}.sqlite");
    }

    private string GetTemporaryDatabasePath()
    {
        return Path.Combine(SettingConfig.MemoryEmbededPath, $"{GetSafeFileName(Name)}.updating.sqlite");
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (string path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string GetSafeFileName(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = StringBuilderPool.Get();
        foreach (char character in name.Trim())
            builder.Append(invalidChars.Contains(character) ? '_' : character);

        string safeName = builder.ToString();
        StringBuilderPool.Release(builder);
        return string.IsNullOrWhiteSpace(safeName) ? GetSourceId(name)[..12] : safeName;
    }

    private static void Report(
        IProgress<MemoryIndexProgress>? progress,
        MemoryIndexStage stage,
        double percentage,
        string source,
        int processedSources,
        int totalSources,
        int currentChunk,
        int totalChunks,
        int failedSources)
    {
        progress?.Report(new MemoryIndexProgress(stage, Math.Clamp(percentage, 0, 1), source,
            processedSources, totalSources, currentChunk, totalChunks, failedSources));
    }

    private sealed record PendingChunk(MemorySourceDocument Document, string Text);

    private sealed class MemoryChunkRecord
    {
        public string Id { get; set; } = "";
        public string SourceName { get; set; } = "";
        public string SourceKind { get; set; } = "";
        public string SourceId { get; set; } = "";
        public int ChunkIndex { get; set; }
        public string Text { get; set; } = "";
        public ReadOnlyMemory<float> Embedding { get; set; }
    }
}
