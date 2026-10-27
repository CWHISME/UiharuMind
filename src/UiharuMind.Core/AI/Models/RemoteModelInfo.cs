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
    /// <summary>思考模式下带 tool_calls 时是否要求原样带回思考正文；<c>null</c>=自动(有思考就回传)，<c>true</c>/<c>false</c>=显式要求/禁止</summary>
    [JsonIgnore] public bool? RequiresReasoningContentRoundtrip => Config.RequiresReasoningContentRoundtrip;

    /// <summary>回填思考正文用的 wire 键名（观测值/预设/默认），请求侧不写死</summary>
    [JsonIgnore] public string ThinkingField => Config.EffectiveThinkingField;
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