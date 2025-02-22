using System.Security.Cryptography;
using System.Text;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.DocumentStorage.DevTools;
using Microsoft.KernelMemory.FileSystem.DevTools;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.KnowledgeBase;

public class MemoryData
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    public List<string> DirectoryPaths { get; set; } = new List<string>();
    public List<string> FilePaths { get; set; } = new List<string>();
    public List<string> UrlPaths { get; set; } = new List<string>();

    private IKernelMemory? _memory;

    public async Task<string> GetLongTermMemory(string query, bool asChunks = true)
    {
        if (_memory == null)
        {
            await TryInitializeMemoryAsync();
        }

        if (_memory == null) return "";

        if (asChunks)
        {
            // Fetch raw chunks, using KM indexes. More tokens to process with the chat history, but only one LLM request.
            SearchResult memories = await _memory.SearchAsync(query, limit: 10);
            return memories.Results.SelectMany(m => m.Partitions).Aggregate("", (sum, chunk) => sum + chunk.Text + "\n")
                .Trim();
        }

        // Use KM to generate an answer. Fewer tokens, but one extra LLM request.
        MemoryAnswer answer = await _memory.AskAsync(query);
        return answer.Result.Trim();
    }

    public async Task MemorizeDocumentsAsync()
    {
        if (_memory == null)
        {
            Log.Error("Memory not initialized");
            return;
        }

        await _memory.ImportTextAsync(Name, documentId: "name");
        await _memory.ImportTextAsync(Description, documentId: "description");
        foreach (var url in UrlPaths)
        {
            var id = GetUrlId(url);
            // Check if the page is already in memory, to avoid importing twice
            if (!await _memory.IsDocumentReadyAsync(id))
            {
                await _memory.ImportWebPageAsync(url, documentId: id);
            }
        }
    }

    private static string GetUrlId(string url)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToUpperInvariant();
    }

    public void TryInitializeMemory(Action onMemoryReady)
    {
        if (string.IsNullOrEmpty(Name))
        {
            Log.Error("Memory name not set");
            return;
        }

        if (_memory == null)
        {
            LlmManager.Instance.RuntimeEngineManager.TryEnsureEmbededServer(cfg =>
            {
                SimpleVectorDbConfig vectorDbConfig = new SimpleVectorDbConfig
                {
                    StorageType = FileSystemTypes.Disk,
                    Directory = Path.Combine(SettingConfig.MemoryPath, Name)
                };
                SimpleFileStorageConfig fileStorageConfig = new SimpleFileStorageConfig
                {
                    StorageType = FileSystemTypes.Disk,
                    Directory = Path.Combine(SettingConfig.MemoryPath, Name)
                };
                _memory = new KernelMemoryBuilder()
                    .WithOpenAITextEmbeddingGeneration(cfg)
                    .WithSimpleVectorDb(vectorDbConfig)
                    .WithSimpleFileStorage(fileStorageConfig)
                    .Build<MemoryServerless>();

                Task.Run(async () =>
                {
                    await MemorizeDocumentsAsync();
                    onMemoryReady();
                });
            });
        }
        else onMemoryReady();
    }

    public async Task TryInitializeMemoryAsync()
    {
        if (string.IsNullOrEmpty(Name))
        {
            Log.Error("Memory name not set");
            return;
        }

        if (_memory == null)
        {
            // 将回调模式转换为 Task
            await WrapTryEnsureEmbededServerAsync();
            await MemorizeDocumentsAsync();
        }
    }

    private Task WrapTryEnsureEmbededServerAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        LlmManager.Instance.RuntimeEngineManager.TryEnsureEmbededServer(cfg =>
        {
            try
            {
                // 同步构建 _memory
                SimpleVectorDbConfig vectorDbConfig = new()
                {
                    StorageType = FileSystemTypes.Disk,
                    Directory = Path.Combine(SettingConfig.MemoryPath, Name)
                };
                SimpleFileStorageConfig fileStorageConfig = new()
                {
                    StorageType = FileSystemTypes.Disk,
                    Directory = Path.Combine(SettingConfig.MemoryPath, Name)
                };
                _memory = new KernelMemoryBuilder()
                    .WithOpenAITextEmbeddingGeneration(cfg)
                    .WithSimpleVectorDb(vectorDbConfig)
                    .WithSimpleFileStorage(fileStorageConfig)
                    .Build<MemoryServerless>();

                // 标记异步操作完成
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
}