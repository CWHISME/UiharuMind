using System.Security.Cryptography;
using System.Text;
using AngleSharp.Html.Parser;
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
    private const int ChunkMaxLength = 3000;
    private const int ChunkOverlap = 300;
    private const string CollectionName = "chunks";
    private static readonly TimeSpan EmbeddingServerStartTimeout = TimeSpan.FromMinutes(2);

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    public List<string> Texts { get; set; } = new List<string>();
    public List<string> FilePaths { get; set; } = new List<string>();
    public List<string> DirectoryPaths { get; set; } = new List<string>();
    public List<string> UrlPaths { get; set; } = new List<string>();
    public bool IndexDirty { get; set; }
    public DateTime? LastIndexedAt { get; set; }
    public string LastIndexError { get; set; } = "";

    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private readonly HttpClient _httpClient = new();
    private UiharaTextEmbeddingGenerator? _embeddingGenerator;
    private SqliteCollection<string, MemoryChunkRecord>? _collection;
    private int? _embeddingDimensions;
    private bool _isVectorStoreUnavailable;
    private bool _indexUpdateHadErrors;

    public async Task<string> GetLongTermMemory(string query, bool asChunks = true)
    {
        try
        {
            if (!await EnsureReadyForSearchAsync()) return "";

            if (_embeddingGenerator == null || string.IsNullOrWhiteSpace(query) || _isVectorStoreUnavailable) return "";

            var queryEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(query);
            var collection = await EnsureCollectionAsync(queryEmbedding.Length);
            if (collection == null) return "";

            List<VectorSearchResult<MemoryChunkRecord>> memories = new();
            await foreach (var result in collection.SearchAsync(queryEmbedding, 10,
                               new VectorSearchOptions<MemoryChunkRecord> { IncludeVectors = false }))
            {
                memories.Add(result);
            }

            if (memories.Count == 0) return "";

            var sb = StringBuilderPool.Get();
            for (int i = 0; i < memories.Count; i++)
            {
                var result = memories[i];
                sb.AppendLine("SourceName: " + result.Record.SourceName);
                sb.AppendLine("PartitionId: " + result.Record.ChunkIndex);
                sb.AppendLine("Relevance: " + result.Score);
                sb.AppendLine("Content: " + result.Record.Text);

                if (i < memories.Count - 1) sb.AppendLine("\n***\n");
            }

            var str = sb.ToString();
            StringBuilderPool.Release(sb);
            return str;
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }

        return "";
    }

    public async Task<bool> UpdateIndexAsync()
    {
        if (_embeddingGenerator == null)
        {
            await EnsureReadyForSearchAsync();
        }

        if (_embeddingGenerator == null)
        {
            if (string.IsNullOrEmpty(LastIndexError)) LastIndexError = "Embedding server is unavailable.";
            IndexDirty = true;
            SaveIndexState();
            Log.Error(LastIndexError);
            return false;
        }

        await _indexLock.WaitAsync();
        try
        {
            LastIndexError = "";
            _indexUpdateHadErrors = false;
            foreach (var text in Texts)
            {
                await ImportTextSourceAsync("Text", "Text", text, GetTextId(text), false);
            }

            await TryImportFile("Files", FilePaths, true);

            List<string> files = new List<string>();
            foreach (var directoryPath in DirectoryPaths)
            {
                if (!Directory.Exists(directoryPath))
                {
                    Log.Warning($"Memory directory not found: {directoryPath}");
                    continue;
                }

                foreach (var filePath in Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories))
                {
                    if ((File.GetAttributes(filePath) & FileAttributes.Hidden) == 0)
                        files.Add(filePath);
                }
            }

            await TryImportFile("DirectoryFiles", files, true);

            foreach (var url in UrlPaths)
            {
                await TryImportUrlAsync(url);
            }

            if (_isVectorStoreUnavailable || _indexUpdateHadErrors)
            {
                IndexDirty = true;
                if (string.IsNullOrEmpty(LastIndexError))
                    LastIndexError = _isVectorStoreUnavailable
                        ? "Memory vector store unavailable"
                        : "Memory index update failed";
                SaveIndexState();
                return false;
            }

            IndexDirty = false;
            LastIndexedAt = DateTime.UtcNow;
            SaveIndexState();
            return true;
        }
        catch (Exception e)
        {
            _indexUpdateHadErrors = true;
            IndexDirty = true;
            LastIndexError = e.Message;
            SaveIndexState();
            Log.Error(e.Message);
            return false;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task TryImportFile(string documentId, IEnumerable<string> filePaths, bool errorLog)
    {
        foreach (var filePath in filePaths)
        {
            await TryImportFile(documentId, filePath, errorLog);
        }
    }

    private async Task TryImportFile(string documentId, string filePath, bool errorLog)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        var id = GetUrlId(filePath);
        try
        {
            if (await IsSourceIndexedAsync(id)) return;

            if (TryReadTextFile(filePath, out string content))
            {
                await ImportTextSourceAsync(filePath, documentId, content, id, true);
            }
            else if (errorLog)
            {
                Log.Warning($"Memory file not supported as text: {filePath}");
            }
        }
        catch (Exception e)
        {
            _indexUpdateHadErrors = true;
            LastIndexError = e.Message;
            if (errorLog) Log.Error(e.Message);
            else Log.Warning(e.Message);
        }
    }

    private async Task TryImportUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        var id = GetUrlId(url);
        if (await IsSourceIndexedAsync(id)) return;

        try
        {
            var html = await _httpClient.GetStringAsync(url);
            var parser = new HtmlParser();
            var document = await parser.ParseDocumentAsync(html);
            var text = document.Body?.TextContent ?? document.DocumentElement.TextContent;
            await ImportTextSourceAsync(url, "Url", text, id, true);
        }
        catch (Exception e)
        {
            _indexUpdateHadErrors = true;
            LastIndexError = e.Message;
            Log.Warning($"Memory url import failed: {url}, {e.Message}");
        }
    }

    public async Task<bool> EnsureReadyForSearchAsync()
    {
        if (string.IsNullOrEmpty(Name))
        {
            Log.Error("Memory name not set");
            LastIndexError = "Memory name not set";
            return false;
        }

        return await WrapTryEnsureEmbededServerAsync();
    }

    public void Save()
    {
        IndexDirty = true;
        LastIndexError = "";
        SaveIndexState();
    }

    private void SaveIndexState()
    {
        MemoryManager.Instance.Save(this);
    }

    private async Task ImportTextSourceAsync(string sourceName, string sourceKind, string content, string sourceId,
        bool errorLog)
    {
        if (_embeddingGenerator == null || string.IsNullOrWhiteSpace(content) || _isVectorStoreUnavailable) return;

        var chunkIndex = 0;
        List<MemoryChunkRecord> chunks = new();
        foreach (var chunk in SplitText(content))
        {
            try
            {
                var embedding = await _embeddingGenerator.GenerateEmbeddingAsync(chunk);
                var collection = await EnsureCollectionAsync(embedding.Length);
                if (collection == null) return;

                if (chunkIndex == 0 && await IsSourceIndexedAsync(sourceId)) return;

                chunks.Add(new MemoryChunkRecord
                {
                    Id = $"{sourceId}_{chunkIndex}",
                    SourceName = sourceName,
                    SourceKind = sourceKind,
                    SourceId = sourceId,
                    ChunkIndex = chunkIndex,
                    Text = chunk,
                    Embedding = embedding
                });
                chunkIndex++;
            }
            catch (Exception e)
            {
                _indexUpdateHadErrors = true;
                LastIndexError = e.Message;
                if (errorLog) Log.Error(e.Message);
                else Log.Warning(e.Message);
                break;
            }
        }

        if (chunks.Count > 0 && _collection != null)
        {
            await _collection.UpsertAsync(chunks);
        }
    }

    private async Task<bool> IsSourceIndexedAsync(string sourceId)
    {
        if (_collection == null || _isVectorStoreUnavailable) return false;

        await foreach (var _ in _collection.GetAsync(x => x.SourceId == sourceId, 1,
                           new FilteredRecordRetrievalOptions<MemoryChunkRecord> { IncludeVectors = false }))
        {
            return true;
        }

        return false;
    }

    private async Task<SqliteCollection<string, MemoryChunkRecord>?> EnsureCollectionAsync(int embeddingDimensions)
    {
        if (_isVectorStoreUnavailable) return null;

        if (_embeddingDimensions != null && _embeddingDimensions != embeddingDimensions)
        {
            Log.Warning(
                $"Memory vector dimension mismatch: current={_embeddingDimensions}, incoming={embeddingDimensions}, memory={Name}");
            _isVectorStoreUnavailable = true;
            return null;
        }

        if (_collection != null) return _collection;

        try
        {
            _embeddingDimensions = embeddingDimensions;
            var databasePath = GetDatabasePath();
            var directory = Path.GetDirectoryName(databasePath);
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

            var store = new SqliteVectorStore($"Data Source={databasePath}", null);
            _collection = store.GetCollection<string, MemoryChunkRecord>(CollectionName, definition);
            await _collection.EnsureCollectionExistsAsync();
            return _collection;
        }
        catch (Exception e)
        {
            _isVectorStoreUnavailable = true;
            LastIndexError = e.Message;
            Log.Warning($"Memory vector store unavailable: {Name}, {e.Message}");
            return null;
        }
    }

    private string GetDatabasePath()
    {
        return Path.Combine(SettingConfig.MemoryEmbededPath, $"{GetSafeFileName(Name)}.sqlite");
    }

    private async Task<bool> WrapTryEnsureEmbededServerAsync()
    {
        if (_embeddingGenerator != null) return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        LlmManager.Instance.RuntimeEngineManager.TryEnsureEmbededServer(cfg =>
        {
            if (_embeddingGenerator != null)
            {
                tcs.TrySetResult(true);
                return;
            }

            if (cfg == null)
            {
                LastIndexError = "Embedding server is unavailable.";
                tcs.TrySetResult(false);
                return;
            }

            try
            {
                _embeddingGenerator = new UiharaTextEmbeddingGenerator(cfg.Endpoint, cfg.EmbeddingModelMaxTokenTotal);
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                LastIndexError = ex.Message;
                Log.Error(ex.Message);
                tcs.TrySetResult(false);
            }
        });

        var timeoutTask = Task.Delay(EmbeddingServerStartTimeout);
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
        if (completedTask == timeoutTask)
        {
            LastIndexError = "Embedding server startup timed out.";
            Log.Error(LastIndexError);
            return false;
        }

        return await tcs.Task;
    }

    private static IEnumerable<string> SplitText(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(normalized)) yield break;

        var start = 0;
        while (start < normalized.Length)
        {
            var length = Math.Min(ChunkMaxLength, normalized.Length - start);
            yield return normalized.Substring(start, length);
            if (start + length >= normalized.Length) break;
            start += Math.Max(1, ChunkMaxLength - ChunkOverlap);
        }
    }

    private static string GetTextId(string text)
    {
        return GetUrlId(text);
    }

    private static string GetUrlId(string url)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToUpperInvariant();
    }

    private static string GetSafeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = StringBuilderPool.Get();
        foreach (var c in name.Trim())
        {
            builder.Append(invalidChars.Contains(c) ? '_' : c);
        }

        var safeName = builder.ToString();
        StringBuilderPool.Release(builder);
        return string.IsNullOrWhiteSpace(safeName) ? GetUrlId(name)[..12] : safeName;
    }

    private static bool TryReadTextFile(string filePath, out string content)
    {
        content = "";
        try
        {
            if (IsTextFile(filePath))
            {
                content = File.ReadAllText(filePath, new UTF8Encoding(false, true));
                return true;
            }

            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsTextFile(string filePath, int sampleSize = 1024)
    {
        try
        {
            byte[] buffer = new byte[sampleSize];
            using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            int bytesRead = fs.Read(buffer, 0, sampleSize);
            if (bytesRead == 0) return true;

            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0) return false;
            }

            new UTF8Encoding(false, true).GetString(buffer, 0, bytesRead);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private class MemoryChunkRecord
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
