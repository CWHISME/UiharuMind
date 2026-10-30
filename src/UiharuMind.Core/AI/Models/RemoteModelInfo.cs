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

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Models;

public class RemoteModelInfo : ILlmModel
{
    public BaseRemoteModelConfig Config { get; set; } = new RemoteModelConfig();

    [JsonIgnore] public string ModelName => Config.ModelName;
    [JsonIgnore] public string ModelPath => Config.ModelPath;
    [JsonIgnore] public bool IsVision => Config.IsVision;
    [JsonIgnore] public string ModelDescription => Config.ModelDescription;
    [JsonIgnore] public string ModelId => Config.ModelId;
    [JsonIgnore] public int Port => Config.Port;
    [JsonIgnore] public int ContextLength => Config.ContextLength;
    [JsonIgnore] public bool OmitSamplingParams => Config.OmitSamplingParams;

    /// <summary>会话亲和写哪几个请求头名；空 = 不发（默认）。供应商配置自行声明</summary>
    [JsonIgnore] public IReadOnlyList<string>? SessionAffinityHeaders => Config.SessionAffinityHeaders;

    /// <summary>是否发 body 级的 <c>prompt_cache_key</c>（默认关，认它的供应商才开）</summary>
    [JsonIgnore] public bool SupportsPromptCacheKey => Config.SupportsPromptCacheKey;
    /// <summary>思考模式下带 tool_calls 时是否要求原样带回思考正文；<c>null</c>=自动(有思考就回传)，<c>true</c>/<c>false</c>=显式要求/禁止</summary>
    [JsonIgnore] public bool? ReplayExistingReasoning => Config.ReplayExistingReasoning;

    /// <summary>回填思考正文用的<b>请求侧</b> wire 键名（预设表/默认）；响应侧观测值不参与</summary>
    [JsonIgnore] public string ThinkingField => Config.RequestThinkingField;

    /// <summary>每一条助手消息都必须带思考字段（服务端硬校验），没正文补空串</summary>
    [JsonIgnore] public bool RequireReasoningOnEveryAssistantMessage =>
        Config.RequireReasoningOnEveryAssistantMessage;

    /// <summary>本次请求的思考档位；请求侧据此判断是否可能处于思考模式</summary>
    [JsonIgnore] public string ThinkingEffort => Config.ThinkingEffort;
    public virtual IReadOnlyList<KeyValuePair<string, JsonNode?>>? GetExtraParams() =>
        Config.GetExtraParams();

    private string _encryptedApiKey = "";

    /// <summary>
    /// 加密后的 ApiKey,序列化到配置文件时使用
    /// </summary>
    [JsonPropertyName("ApiKey")]
    public string EncryptedApiKey
    {
        get => _encryptedApiKey;
        set => _encryptedApiKey = value;
    }

    /// <summary>
    /// ApiKey 明文,读取时自动解密,写入时加密存储
    /// </summary>
    [JsonIgnore]
    public string ApiKey
    {
        get => AesEncryptionUtils.DecryptString(_encryptedApiKey);
        set => _encryptedApiKey = AesEncryptionUtils.EncryptString(value);
    }
}