/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UiharuMind.Core.AI.Embedding;

public sealed class OpenAICompatibleEmbeddingSession : IEmbeddingSession
{
    private readonly HttpClient _httpClient;
    private readonly string _modelId;
    private bool _disposed;

    public OpenAICompatibleEmbeddingSession(string endpoint, string modelId, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new EmbeddingRuntimeException("Remote embedding endpoint is not set.");
        if (string.IsNullOrWhiteSpace(modelId))
            throw new EmbeddingRuntimeException("Remote embedding model id is not set.");

        _modelId = modelId;
        _httpClient = new HttpClient { BaseAddress = CreateEmbeddingUri(endpoint) };
        if (!string.IsNullOrWhiteSpace(apiKey))
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public string BackendName => "OpenAICompatible";
    public string ModelPath => _httpClient.BaseAddress?.ToString() ?? "";
    public int Dimensions { get; private set; }
    public bool IsRunning => !_disposed;
    public string LastError { get; private set; } = "";

    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text)) return ReadOnlyMemory<float>.Empty;

        var request = new EmbeddingRequest(_modelId, text);
        string requestJson = JsonSerializer.Serialize(request);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        using HttpResponseMessage response =
            await _httpClient.PostAsync("", content, cancellationToken).ConfigureAwait(false);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if (IsInputTooLargeError(responseJson))
                throw new EmbeddingInputTooLargeException(responseJson);

            LastError = responseJson;
            throw new HttpRequestException(
                $"Embedding request failed ({(int)response.StatusCode}): {responseJson}",
                null, response.StatusCode);
        }

        float[] vector = ParseEmbedding(responseJson);
        EmbeddingVectorUtils.NormalizeInPlace(vector);
        Dimensions = vector.Length;
        LastError = "";
        return vector;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }

    private static Uri CreateEmbeddingUri(string endpoint)
    {
        var builder = new UriBuilder(endpoint);
        string path = builder.Path.TrimEnd('/');
        if (path.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase))
            builder.Path = path;
        else if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            builder.Path = path + "/embeddings";
        else if (string.IsNullOrEmpty(path))
            builder.Path = "/v1/embeddings";
        return builder.Uri;
    }

    private static float[] ParseEmbedding(string responseJson)
    {
        using JsonDocument document = JsonDocument.Parse(responseJson);
        JsonElement root = document.RootElement;
        JsonElement embedding = root.GetProperty("data")[0].GetProperty("embedding");
        float[]? vector = embedding.Deserialize<float[]>();
        return vector ?? throw new InvalidOperationException("Invalid embedding response.");
    }

    private static bool IsInputTooLargeError(string responseBody)
    {
        return responseBody.Contains("input is too large", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("increase the physical batch size", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input);
}
