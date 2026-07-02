/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CliWrap;
using CliWrap.EventStream;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.AI.Runtime.Backends;

public sealed class LLamaCppEmbeddingSession : IEmbeddingSession
{
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

    public string BackendName => LLamaCppRuntimeBackend.ProviderId;
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
        if (string.IsNullOrWhiteSpace(version.ExecutablePath))
            throw new EmbeddingRuntimeException("llama.cpp runtime path is not set.");
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            throw new FileNotFoundException("Embedding model file not found.", modelPath);

        string? executablePath = config.GetExeServerPath(version.ExecutablePath);
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            throw new FileNotFoundException("llama.cpp server executable was not found.", executablePath);

        int port = config.DefaultEmbededPort;
        string endpoint = $"http://127.0.0.1:{port}/";
        using var linkedStartupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var serverCts = new CancellationTokenSource();
        var listeningTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        string args = BuildArguments(modelPath, port, parameters);
        Log.Debug("Start llama.cpp embedding server: " + args);

        Task serverTask = Task.Run(async () =>
        {
            try
            {
                await foreach (CommandEvent cmdEvent in Cli.Wrap(executablePath)
                                   .WithArguments(args)
                                   .WithValidation(CommandResultValidation.None)
                                   .ListenAsync(serverCts.Token)
                                   .ConfigureAwait(false))
                {
                    switch (cmdEvent)
                    {
                        case StandardOutputCommandEvent stdOut:
                            HandleServerLog(stdOut.Text, listeningTcs);
                            break;
                        case StandardErrorCommandEvent stdErr:
                            HandleServerLog(stdErr.Text, listeningTcs);
                            break;
                        case ExitedCommandEvent exited:
                            if (!listeningTcs.Task.IsCompleted)
                            {
                                listeningTcs.TrySetException(new EmbeddingRuntimeException(
                                    $"llama.cpp embedding server exited before it was ready. Exit code: {exited.ExitCode}."));
                            }

                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Log.Error($"llama.cpp embedding server failed: {e.Message}");
                listeningTcs.TrySetException(new EmbeddingRuntimeException(
                    $"llama.cpp embedding server failed: {e.Message}", e));
            }
        }, CancellationToken.None);

        await using (linkedStartupCts.Token.Register(() =>
                     listeningTcs.TrySetCanceled(linkedStartupCts.Token)))
        {
            try
            {
                await listeningTcs.Task.WaitAsync(TimeSpan.FromSeconds(120), linkedStartupCts.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                serverCts.Cancel();
                throw;
            }
        }

        return new LLamaCppEmbeddingSession(modelPath, new Uri(endpoint), serverCts, serverTask);
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

    private static string BuildArguments(
        string modelPath,
        int port,
        RuntimeResolvedParameters parameters)
    {
        // embedding server 使用独立参数，避免复用聊天配置时夹带 prompt/sampling 等无关参数。
        return string.Join(" ", new[]
        {
            $"-m \"{modelPath}\"",
            "--no-webui",
            $"--alias \"{Path.GetFileNameWithoutExtension(modelPath)}\"",
            $"--port {port}",
            "-to 0",
            "--embedding",
            "--pooling mean",
            $"--ctx-size {Math.Max(1, parameters.ContextSize)}",
            $"--batch-size {Math.Max(1, parameters.BatchSize)}",
            $"--ubatch-size {Math.Max(1, parameters.UBatchSize)}",
            $"--gpu-layers {parameters.GpuLayers}"
        });
    }

    private static void HandleServerLog(string message, TaskCompletionSource listeningTcs)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Log.Debug(message);
        if (message.Contains("server is listening", StringComparison.OrdinalIgnoreCase))
            listeningTcs.TrySetResult();
        else if (message.Contains("error", StringComparison.OrdinalIgnoreCase) &&
                 !listeningTcs.Task.IsCompleted)
            Log.Error(message);
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
