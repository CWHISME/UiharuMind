namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

using System.Text.Json.Serialization;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net.Transport;

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

    /// <summary>
    /// 这个模型答话走哪种 wire 格式，按目录声明的端点推。
    ///
    /// 只声明了 <c>/responses</c> 的走 responses 线；只声明了 <c>/messages</c> 的走 Anthropic 线；
    /// 声明了 <c>/chat/completions</c>（或都没声明端点字段）的走 chat 线。
    ///
    /// <b>多条都声明时取 chat</b>：chat 是已经跑通的形状，另两条还在接。
    /// 选没跑通的那条当默认，等于让所有模型突然换协议。
    ///
    /// 端点后缀从 <see cref="WireProtocols"/> 取，不在这里再写一份字面量——
    /// 加协议或改路径时只改那张表。放在目录条目上而不是编辑窗口里：这是「目录说了什么」，
    /// 与「界面怎么用」无关
    /// </summary>
    /// <returns>该模型该走的格式</returns>
    public EApiFormat ResolveApiFormat()
    {
        if (SupportedEndpoints.Count == 0) return EApiFormat.ChatCompletions;
        if (SupportsEndpoint(WireProtocols.Chat.EndpointSuffix)) return EApiFormat.ChatCompletions;
        // messages 排在 responses 前面：两条判据互不重叠，顺序只为把更具体的放前面
        if (SupportsEndpoint(WireProtocols.Messages.EndpointSuffix)) return EApiFormat.Messages;
        return SupportsEndpoint(WireProtocols.Responses.EndpointSuffix)
            ? EApiFormat.Responses
            : EApiFormat.ChatCompletions;
    }

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
