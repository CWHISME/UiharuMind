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
using UiharuMind.Core.AI.Embedding;

namespace UiharuMind.Core.AI.Runtime.Backends;

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
        if (string.IsNullOrWhiteSpace(text)) return ReadOnlyMemory<float>.Empty;

        IReadOnlyList<ReadOnlyMemory<float>> vectors =
            await RequestAsync([text], cancellationToken).ConfigureAwait(false);
        return vectors.Count > 0 ? vectors[0] : ReadOnlyMemory<float>.Empty;
    }

    /// <summary>
    /// 一次请求把整批文本发出去。OpenAI 的 <c>input</c> 本来就接受字符串数组,
    /// 逐条发只是白付网络往返——索引一份长文档能差出上千次请求。
    /// </summary>
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        // 空白文本不能发给端点(多数会 400),但返回的向量数必须与入参一一对应,所以就地补空向量
        List<string> payload = [];
        List<int> payloadIndices = [];
        for (int index = 0; index < texts.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(texts[index])) continue;
            payload.Add(texts[index]);
            payloadIndices.Add(index);
        }

        ReadOnlyMemory<float>[] result = new ReadOnlyMemory<float>[texts.Count];
        Array.Fill(result, ReadOnlyMemory<float>.Empty);
        if (payload.Count == 0) return result;

        IReadOnlyList<ReadOnlyMemory<float>> vectors =
            await RequestAsync(payload, cancellationToken).ConfigureAwait(false);
        if (vectors.Count != payload.Count)
        {
            throw new EmbeddingRuntimeException(
                $"Embedding response count mismatch: expected {payload.Count}, got {vectors.Count}.");
        }

        for (int index = 0; index < vectors.Count; index++) result[payloadIndices[index]] = vectors[index];
        return result;
    }

    private async Task<IReadOnlyList<ReadOnlyMemory<float>>> RequestAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var request = new EmbeddingRequest(_modelId, texts);
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

        List<ReadOnlyMemory<float>> vectors = ParseEmbeddings(responseJson);
        if (vectors.Count > 0) Dimensions = vectors[0].Length;
        LastError = "";
        return vectors;
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

    /// <summary>
    /// 解析批量响应。<c>data</c> 里每项带 <c>index</c> 指回入参下标,规范未保证数组顺序,
    /// 按下标归位而不是按出现顺序——顺序错了不会报错,只会让每块配上别块的向量。
    /// </summary>
    internal static List<ReadOnlyMemory<float>> ParseEmbeddings(string responseJson)
    {
        using JsonDocument document = JsonDocument.Parse(responseJson);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            throw new EmbeddingRuntimeException("Invalid embedding response: no data array.");
        }

        var vectors = new List<ReadOnlyMemory<float>>(data.GetArrayLength());
        for (int i = 0; i < data.GetArrayLength(); i++) vectors.Add(ReadOnlyMemory<float>.Empty);

        int fallbackIndex = 0;
        foreach (JsonElement item in data.EnumerateArray())
        {
            float[]? vector = item.GetProperty("embedding").Deserialize<float[]>();
            if (vector == null) throw new EmbeddingRuntimeException("Invalid embedding response.");

            EmbeddingVectorUtils.NormalizeInPlace(vector);
            int index = item.TryGetProperty("index", out JsonElement indexElement) &&
                        indexElement.TryGetInt32(out int parsed) &&
                        parsed >= 0 && parsed < vectors.Count
                ? parsed
                : fallbackIndex;

            vectors[index] = vector;
            fallbackIndex++;
        }

        return vectors;
    }

    private static bool IsInputTooLargeError(string responseBody)
    {
        return responseBody.Contains("input is too large", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("increase the physical batch size", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);
}
