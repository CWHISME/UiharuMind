/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Runtime.Backends;

public sealed class LLamaCppEmbeddingSession : IEmbeddingSession
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(2);

    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _serverCts;
    private readonly Task _serverTask;
    private readonly SemaphoreSlim _generationLock = new(1, 1);
    private bool _disposed;

    private LLamaCppEmbeddingSession(
        string modelPath,
        Uri endpoint,
        CancellationTokenSource serverCts,
        Task serverTask)
    {
        ModelPath = modelPath;
        _serverCts = serverCts;
        _serverTask = serverTask;
        _httpClient = new HttpClient { BaseAddress = new Uri(endpoint, "v1/embeddings") };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "None");
    }

    public string BackendName => LLamaCppRuntimeBackend.BackendId;
    public string ModelPath { get; }
    public int Dimensions { get; private set; }
    public bool IsRunning => !_disposed && !_serverTask.IsCompleted;
    public string LastError { get; private set; } = "";

    public static async Task<LLamaCppEmbeddingSession> StartAsync(
        VersionInfo version,
        LLamaCppSettingConfig config,
        string modelPath,
        RuntimeResolvedParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            throw new FileNotFoundException("Embedding model file not found.", modelPath);

        CancellationTokenSource serverCts = new();
        using CancellationTokenRegistration startupCancel = cancellationToken.Register(serverCts.Cancel);
        try
        {
            LlamaServerProcess server = await LlamaServerProcess.StartAsync(
                    config.GetExeServerPath(version.ExecutablePath) ?? "",
                    BuildArguments(modelPath, parameters),
                    ReadyTimeout,
                    null,
                    serverCts.Token)
                .ConfigureAwait(false);
            return new LLamaCppEmbeddingSession(modelPath, server.BaseUri, serverCts, server.Completion);
        }
        catch
        {
            serverCts.Dispose();
            throw;
        }
    }

    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text)) return ReadOnlyMemory<float>.Empty;

        await _generationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var request = new EmbeddingRequest("UiharuMind", text);
            string requestJson = JsonSerializer.Serialize(request);
            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using HttpResponseMessage response =
                await _httpClient.PostAsync("", content, cancellationToken).ConfigureAwait(false);
            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LastError = responseJson;
                if (IsInputTooLargeError(responseJson))
                    throw new EmbeddingInputTooLargeException(responseJson);

                throw new HttpRequestException(
                    $"llama.cpp embedding request failed ({(int)response.StatusCode}): {responseJson}",
                    null,
                    response.StatusCode);
            }

            float[] vector = ParseEmbedding(responseJson);
            EmbeddingVectorUtils.NormalizeInPlace(vector);
            Dimensions = vector.Length;
            LastError = "";
            return vector;
        }
        catch (EmbeddingInputTooLargeException)
        {
            throw;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            throw new EmbeddingRuntimeException($"llama.cpp embedding request failed: {e.Message}", e);
        }
        finally
        {
            _generationLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _serverCts.Cancel();
        _httpClient.Dispose();
        _serverCts.Dispose();
        _generationLock.Dispose();
    }

    // 嵌入服务用独立参数，避免复用对话配置时夹带无关参数
    private static IReadOnlyList<string> BuildArguments(string modelPath, RuntimeResolvedParameters parameters)
    {
        return
        [
            "-m", modelPath,
            "--no-webui",
            "--alias", Path.GetFileNameWithoutExtension(modelPath),
            "-to", "0",
            "--embedding",
            "--pooling", "mean",
            "--ctx-size", Math.Max(1, parameters.ContextSize).ToString(),
            "--batch-size", Math.Max(1, parameters.BatchSize).ToString(),
            "--ubatch-size", Math.Max(1, parameters.UBatchSize).ToString(),
            "--gpu-layers", parameters.GpuLayers.ToString()
        ];
    }

    private static float[] ParseEmbedding(string responseJson)
    {
        using JsonDocument document = JsonDocument.Parse(responseJson);
        JsonElement root = document.RootElement;
        JsonElement embedding = root.GetProperty("data")[0].GetProperty("embedding");
        float[]? vector = embedding.Deserialize<float[]>();
        return vector ?? throw new InvalidOperationException("Invalid llama.cpp embedding response.");
    }

    private static bool IsInputTooLargeError(string message)
    {
        return message.Contains("input is too large", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("increase the physical batch size", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("context", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("overflow", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input);
}
