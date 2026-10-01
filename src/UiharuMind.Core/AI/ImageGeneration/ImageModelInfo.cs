using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 接口格式：各家都称 OpenAI 兼容，到出图接口却各说各话，一种格式一个适配器
/// </summary>
public enum EImageDialect
{
    /// <summary>OpenAI 官方 <c>/images/*</c>：生成 JSON、编辑 multipart</summary>
    OpenAI,

    /// <summary>商汤：生成与编辑分两个路径，编辑也是 JSON，尺寸写 <c>宽x高</c></summary>
    SenseNova,

    /// <summary>Agnes：生成与编辑同一路径，尺寸写档位 + 比例</summary>
    Agnes,
}

/// <summary>
/// 一个生图模型。单独登记，不进对话模型那张表（见 ADR 0052）。
/// </summary>
public sealed class ImageModelInfo
{
    private string _encryptedApiKey = "";

    /// <summary>显示名，也是熔断与回退报告里的称呼；列表内唯一</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>接口格式</summary>
    public EImageDialect Dialect { get; set; } = EImageDialect.OpenAI;

    /// <summary>服务根地址（如 <c>https://token.sensenova.cn/v1</c>），接口路径由接口格式补</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>发给接口的模型 id</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>分辨率档</summary>
    public EImageResolution Resolution { get; set; } = EImageResolution.Res2K;

    /// <summary>支持编辑（带输入图）。不支持的在编辑请求里直接跳过，不算失败</summary>
    public bool SupportsEditing { get; set; } = true;

    /// <summary>单次请求超时（秒）。出图慢，Agnes 建议 60~360</summary>
    public int TimeoutSeconds { get; set; } = 360;

    /// <summary>
    /// 并进请求正文顶层的私有参数（JSON 对象文本，如 <c>{"watermark": false}</c>），同名键覆盖我们的取值
    /// </summary>
    public string ExtraBody { get; set; } = string.Empty;

    /// <summary>加密后的 ApiKey，序列化到配置文件时使用</summary>
    [JsonInclude]
    [JsonPropertyName("ApiKey")]
    private string EncryptedApiKey
    {
        get => _encryptedApiKey;
        set => _encryptedApiKey = value;
    }

    /// <summary>ApiKey 明文，读取时解密、写入时加密</summary>
    [JsonIgnore]
    public string ApiKey
    {
        get => AesEncryptionUtils.DecryptString(_encryptedApiKey);
        set => _encryptedApiKey = AesEncryptionUtils.EncryptString(value);
    }

    /// <summary>地址与模型 id 都填了。密钥可空——自托管的兼容服务常常不鉴权</summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ModelId);

    /// <summary>
    /// 解析私有参数
    /// </summary>
    /// <param name="body">解析结果；为空时是空对象</param>
    /// <param name="error">解析失败的原因</param>
    /// <returns>为空或是合法 JSON 对象返回 true</returns>
    public bool TryParseExtraBody(out JsonObject body, out string? error)
    {
        body = new JsonObject();
        error = null;
        if (string.IsNullOrWhiteSpace(ExtraBody)) return true;

        try
        {
            if (JsonNode.Parse(ExtraBody) is JsonObject parsed)
            {
                body = parsed;
                return true;
            }

            error = "extra body must be a JSON object";
        }
        catch (JsonException e)
        {
            error = $"extra body is not valid JSON: {e.Message}";
        }

        return false;
    }
}
