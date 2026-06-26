using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Embeded;

public class UiharaTextEmbeddingGenerator
{
    public UiharaTextEmbeddingGenerator(string baseUrl, int maxTokens)
    {
        this.MaxTokens = maxTokens;
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public int MaxTokens { get; }

    private readonly HttpClient _httpClient;

    public int CountTokens(string text)
    {
        return 0;
    }

    public IReadOnlyList<string> GetTokens(string text)
    {
        return new List<string>();
    }

    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        // 准备请求数据
        var requestData = new EmbeddingRequest
        {
            Input = text
        };

        // 将请求数据序列化为 JSON
        var requestJson = JsonSerializer.Serialize(requestData);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        // 发送 POST 请求
        using HttpResponseMessage response =
            await _httpClient.PostAsync("/embedding", content, cancellationToken).ConfigureAwait(false);
        string responseJson =
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (IsInputTooLargeError(responseJson))
                throw new EmbeddingInputTooLargeException(responseJson);

            throw new HttpRequestException(
                $"Embedding request failed ({(int)response.StatusCode}): {responseJson}",
                null, response.StatusCode);
        }

        // 解析 JSON 响应
        foreach (var node in JsonDocument.Parse(responseJson).RootElement[0].EnumerateObject())
        {
            if (node.Name == "embedding")
            {
                var embedding = JsonSerializer.Deserialize<float[]>(node.Value[0].GetRawText());
                return new ReadOnlyMemory<float>(embedding ?? throw new InvalidOperationException("Invalid embedding data"));
            }
        }

        throw new InvalidOperationException("Invalid response data");
    }

    private static bool IsInputTooLargeError(string responseBody)
    {
        return responseBody.Contains("input is too large", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("increase the physical batch size", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase);
    }

    private class EmbeddingRequest
    {
        [JsonPropertyName("input")] public string Input { get; set; } = "";
    }
}

public sealed class EmbeddingInputTooLargeException : Exception
{
    public EmbeddingInputTooLargeException(string message) : base(message)
    {
    }
}
