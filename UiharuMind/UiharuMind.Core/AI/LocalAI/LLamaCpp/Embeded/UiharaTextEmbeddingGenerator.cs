using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI;

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Embeded;

public class UiharaTextEmbeddingGenerator : ITextEmbeddingGenerator
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

    public async Task<Embedding> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
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
        var response = await _httpClient.PostAsync("/embedding", content, cancellationToken);

        // 确保请求成功
        response.EnsureSuccessStatusCode();

        // 读取响应内容
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        // 解析 JSON 响应
        foreach (var node in JsonDocument.Parse(responseJson).RootElement[0].EnumerateObject())
        {
            if (node.Name == "embedding")
            {
                var embedding = JsonSerializer.Deserialize<float[]>(node.Value[0].GetRawText());
                return new Embedding(embedding ?? throw new InvalidOperationException("Invalid embedding data"));
            }
        }

        throw new InvalidOperationException("Invalid response data");
    }

    private class EmbeddingRequest
    {
        [JsonPropertyName("input")] public string Input { get; set; } = "";
    }
}