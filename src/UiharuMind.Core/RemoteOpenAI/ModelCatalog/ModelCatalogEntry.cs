namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

using System.Text.Json.Serialization;

/// <summary>
/// 模型目录里的一条：服务商 /v1/models 返回的模型候选。
/// 属性名与 OpenAI 系响应一致（读取大小写不敏感）。
///
/// 标准 /v1/models 只给 id/name/context_length；部分服务商（如商汤）额外带
/// <c>input_modalities</c> / <c>max_output_length</c> / <c>supported_sampling_parameters</c>，
/// 可直接预填视觉、输出预算与采样参数（ADR 0092 补记）。
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

    /// <summary>输入模态（text/image/audio/video/pdf）；标准 /v1/models 不返回</summary>
    [JsonPropertyName("input_modalities")]
    public List<string> InputModalities { get; set; } = [];

    /// <summary>输出模态；标准 /v1/models 不返回。非 text 输出（如出图模型）不作为对话候选</summary>
    [JsonPropertyName("output_modalities")]
    public List<string> OutputModalities { get; set; } = [];

    /// <summary>服务端允许的最大输出长度（max_tokens 上限）；0 表示未声明</summary>
    [JsonPropertyName("max_output_length")]
    public int MaxOutputLength { get; set; }

    /// <summary>服务端支持的采样参数；声明后不含 temperature 即表示该模型固定采样参数</summary>
    [JsonPropertyName("supported_sampling_parameters")]
    public List<string> SupportedSamplingParameters { get; set; } = [];

    /// <summary>是否能在指定端点答话（忽略大小写、按子串匹配）</summary>
    public bool SupportsEndpoint(string endpoint) =>
        SupportedEndpoints.Any(e => e.Contains(endpoint, StringComparison.OrdinalIgnoreCase));

    /// <summary>目录是否携带能力字段（商汤等富目录）</summary>
    public bool HasCapabilities =>
        InputModalities.Count > 0 || MaxOutputLength > 0 || SupportedSamplingParameters.Count > 0;

    /// <summary>是否接受图片输入</summary>
    public bool SupportsVision => Contains(InputModalities, "image");

    /// <summary>是否产出文本；输出模态未声明时按对话模型处理</summary>
    public bool SupportsTextOutput => OutputModalities.Count == 0 || Contains(OutputModalities, "text");

    /// <summary>是否应省略采样参数（声明了支持的采样参数、但不含 temperature）</summary>
    public bool OmitsSamplingParams =>
        SupportedSamplingParameters.Count > 0 && !Contains(SupportedSamplingParameters, "temperature");

    private static bool Contains(List<string> values, string value) =>
        values.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
}
