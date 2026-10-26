namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

using System.Text.Json.Serialization;

/// <summary>
/// 模型目录里的一条：服务商 /v1/models 返回的模型候选。
/// 属性名与 OpenAI 系响应一致（读取大小写不敏感）。
/// </summary>
public sealed class ModelCatalogEntry
{
    /// <summary>请求时要原样发送的模型 ID（如 deepseek/deepseek-v4-flash）</summary>
    public string Id { get; set; } = "";

    /// <summary>显示名（如 DeepSeek V4 Flash (latest)），可为空</summary>
    public string Name { get; set; } = "";

    /// <summary>模型上下文窗口 token 数</summary>
    [JsonPropertyName("context_length")]
    public int ContextLength { get; set; }

    /// <summary>该模型答话可用的端点（/chat/completions、/messages 等）</summary>
    [JsonPropertyName("supported_endpoints")]
    public List<string> SupportedEndpoints { get; set; } = [];

    /// <summary>是否能在指定端点答话（忽略大小写、按子串匹配）</summary>
    public bool SupportsEndpoint(string endpoint) =>
        SupportedEndpoints.Any(e => e.Contains(endpoint, StringComparison.OrdinalIgnoreCase));
}
