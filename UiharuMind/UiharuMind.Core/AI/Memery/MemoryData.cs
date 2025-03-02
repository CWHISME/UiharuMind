using System.Security.Cryptography;
using System.Text;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Configuration;
using Microsoft.KernelMemory.DocumentStorage.DevTools;
using Microsoft.KernelMemory.FileSystem.DevTools;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using Microsoft.KernelMemory.Pipeline;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Embeded;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Memery;

public class MemoryData : IUniquieContainerItem
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    public List<string> Texts { get; set; } = new List<string>();
    public List<string> FilePaths { get; set; } = new List<string>();
    public List<string> DirectoryPaths { get; set; } = new List<string>();
    public List<string> UrlPaths { get; set; } = new List<string>();

    private IKernelMemory? _memory;

    public async Task<string> GetLongTermMemory(string query, bool asChunks = true)
    {
        await TryInitializeMemoryAsync();

        if (_memory == null) return "";

        if (asChunks)
        {
            // Fetch raw chunks, using KM indexes. More tokens to process with the chat history, but only one LLM request.
            SearchResult memories = await _memory.SearchAsync(query, limit: 10);
            var sb = StringBuilderPool.Get();

            for (int i = 0; i < memories.Results.Count; i++)
            {
                var result = memories.Results[i];
                sb.AppendLine("SourceName: " + result.SourceName);

                for (int indexPart = 0; indexPart < result.Partitions.Count; indexPart++)
                {
                    var partition = result.Partitions[indexPart];
                    sb.AppendLine("PartitionId: " + partition.PartitionNumber);
                    sb.AppendLine("Relevance: " + partition.Relevance);
                    sb.AppendLine("Content: " + partition.Text);
                    if (indexPart < result.Partitions.Count - 1) sb.AppendLine("\n");
                }

                if (i < memories.Results.Count - 1) sb.AppendLine("\n***\n");
            }

            var str = sb.ToString();
            StringBuilderPool.Release(sb);
            return str;
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

        // await _memory.ImportTextAsync(Name, documentId: "name");
        // if (!string.IsNullOrEmpty(Description)) await _memory.ImportTextAsync(Description, documentId: "description");

        foreach (var text in Texts)
        {
            var id = text.GetHashCode().ToString();
            if (!await _memory.IsDocumentReadyAsync(id))
                await _memory.ImportTextAsync(text);
        }

        // await _memory.ImportDocumentAsync(new Document("FilePaths", filePaths: FilePaths));
        // foreach (var filePath in FilePaths)
        // {
        //     await TryImportFile(_memory, filePath, true);
        // }
        await TryImportFile(_memory, "Files", FilePaths, true);

        List<string> files = new List<string>();
        foreach (var directoryPath in DirectoryPaths)
        {
            // files.AddRange();
            // foreach (var filePath in Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories))
            // {
            //     await TryImportFile(_memory, filePath, true);
            // }
            // await TryImportFile(_memory,"DirectoryFilePaths", filePath, true);
            foreach (var filePath in Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(filePath) & FileAttributes.Hidden) == 0)
                    files.Add(filePath);
            }
        }

        await TryImportFile(_memory, "DirectoryFiles", files, true);

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

    private async Task TryImportFile(IKernelMemory memory, string documentId, IEnumerable<string> filePaths,
        bool errorLog)
    {
        // try
        // {
        //     // var fileName = Path.GetFileNameWithoutExtension(filePath);
        //     await memory.ImportDocumentAsync(new Document(documentId, filePaths: filePaths));
        //     // await memory.ImportDocumentAsync(filePath, documentId: fileName);
        // }
        // catch (Exception e)
        // {
        //     if (errorLog) Log.Error(e.Message);
        //     else Log.Warning(e.Message);
        // }
        foreach (var filePath in filePaths)
        {
            await TryImportFile(memory, documentId, filePath, errorLog);
        }
    }

    private async Task TryImportFile(IKernelMemory memory, string documentId, string filePath, bool errorLog)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        var id = GetUrlId(filePath);
        try
        {
            // var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (!await memory.IsDocumentReadyAsync(id))
                await memory.ImportDocumentAsync(filePath, documentId: id);
        }
        catch (MimeTypeException mimeTypeException)
        {
            //不支持的文件，尝试当做文本文件导入
            if (TryReadTextFile(filePath, out string content))
            {
                Log.Debug($"Embeding: File not supported, try import as text file: {filePath}");
                // var id = content.GetHashCode().ToString();
                if (!await memory.IsDocumentReadyAsync(id))
                    await memory.ImportTextAsync(content, documentId: id);
            }
            else if (errorLog)
            {
                Log.Error(mimeTypeException.Message);
            }
            else Log.Warning(mimeTypeException.Message);
        }
        catch (Exception e)
        {
            if (errorLog) Log.Error(e.Message);
            else Log.Warning(e.Message);
        }
    }

    public async Task TryInitializeMemoryAsync()
    {
        if (string.IsNullOrEmpty(Name))
        {
            Log.Error("Memory name not set");
            return;
        }

        // 将回调模式转换为 Task
        await WrapTryEnsureEmbededServerAsync();

        if (_memory != null)
        {
            await MemorizeDocumentsAsync();
        }
    }

    public void Save()
    {
        MemoryManager.Instance.Save(this);
    }

    private static string GetUrlId(string url)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToUpperInvariant();
    }

    private Task WrapTryEnsureEmbededServerAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        LlmManager.Instance.RuntimeEngineManager.TryEnsureEmbededServer(cfg =>
        {
            if (cfg == null || _memory != null)
            {
                tcs.SetResult(false);
                return;
            }

            try
            {
                // 同步构建 _memory
                SimpleVectorDbConfig vectorDbConfig = new()
                {
                    StorageType = FileSystemTypes.Disk,
                    Directory = Path.Combine(SettingConfig.MemoryEmbededPath, Name)
                };
                SimpleFileStorageConfig fileStorageConfig = new()
                {
                    StorageType = FileSystemTypes.Disk,
                    Directory = Path.Combine(SettingConfig.MemoryEmbededPath, Name)
                };
                // var ubatchSize = LlmManager.Instance.LLamaCppServer.Config.ParamsConfig.UbatchSize;
                _memory = new KernelMemoryBuilder()
                    // .WithCustomTextPartitioningOptions(
                    //     new TextPartitioningOptions
                    //     {
                    //         MaxTokensPerParagraph = ubatchSize,
                    //         OverlappingTokens = (int)(ubatchSize * 0.1f)
                    //     })
                    // .WithOpenAITextEmbeddingGeneration(cfg)
                    .WithCustomEmbeddingGenerator(
                        new UiharaTextEmbeddingGenerator(cfg.Endpoint, cfg.EmbeddingModelMaxTokenTotal))
                    .WithoutTextGenerator()
                    // .WithOpenAITextGeneration(new OpenAIConfig()
                    // {
                    //     Endpoint = $"http://localhost:{LlmManager.Instance.LLamaCppServer.Config.DefautPort}/",
                    //     APIKey = "None", TextModel = "None"
                    // })
                    //.WithStructRagSearchClient()
                    .WithSimpleVectorDb(vectorDbConfig)
                    .WithSimpleFileStorage(fileStorageConfig)
                    .Build<MemoryServerless>();

                // 标记异步操作完成
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
            }
        });

        return tcs.Task;
    }

    private static bool TryReadTextFile(string filePath, out string content)
    {
        content = "";
        try
        {
            if (IsTextFile(filePath))
            {
                content = File.ReadAllText(filePath, Encoding.UTF8);
                return true;
            }

            return false;
        }
        catch (DecoderFallbackException)
        {
            return false; // 解码失败
        }
    }

    private static bool IsTextFile(string filePath, int sampleSize = 1024)
    {
        try
        {
            byte[] buffer = new byte[sampleSize];
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                int bytesRead = fs.Read(buffer, 0, sampleSize);
                int textCharCount = 0;
                int nullByteCount = 0;

                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = buffer[i];
                    // 检查是否为可打印ASCII字符（排除控制字符，保留换行、回车、制表符）
                    if (b >= 0x20 && b <= 0x7E || b == 0x09 || b == 0x0A || b == 0x0D)
                    {
                        textCharCount++;
                    }
                    else if (b == 0x00)
                    {
                        nullByteCount++; // NULL字节常见于二进制文件
                    }
                }

                // 如果存在NULL字节，判定为二进制文件
                if (nullByteCount > 0)
                    return false;

                // 若可打印字符占比超过95%，认为是文本文件
                double textRatio = (double)textCharCount / bytesRead;
                return textRatio > 0.95;
            }
        }
        catch
        {
            return false;
        }
    }
}