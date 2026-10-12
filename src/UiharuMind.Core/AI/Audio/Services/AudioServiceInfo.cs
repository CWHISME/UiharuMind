using System.Text.Json.Serialization;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 远程音频服务的接口格式。各家都称 OpenAI 兼容，到音频接口却各说各话，一种格式一个适配器
/// （照 <see cref="UiharuMind.Core.AI.ImageGeneration.EImageDialect"/> 的先例）。
/// </summary>
public enum EAudioDialect
{
    /// <summary>OpenAI 官方形状：/v1/audio/speech、/v1/audio/transcriptions</summary>
    OpenAICompatible,

    /// <summary>Edge TTS（免密钥，微软 Edge 朗读的非官方端点，只会合成）</summary>
    EdgeTts
}

/// <summary>
/// 一个远程音频服务，单独登记，不进对话模型那张表（照 ADR 0052 生图模型先例）。
/// 一条只做一件事（合成或识别）：各家两件事用的模型 id 不同。新建时从同家对话模型复制密钥预填，不引用。
/// </summary>
public sealed class AudioServiceInfo
{
    /// <summary>
    /// 服务在「用哪个」里的 id 前缀，后接 <see cref="Name"/>
    /// </summary>
    public const string IdPrefix = "service:";

    /// <summary>显示名，列表内唯一</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>接口格式</summary>
    public EAudioDialect Dialect { get; set; } = EAudioDialect.OpenAICompatible;

    /// <summary>做什么：合成或识别</summary>
    public EAudioCapability Task { get; set; } = EAudioCapability.Tts;

    /// <summary>服务根地址（如 https://api.openai.com/v1），接口路径由接口格式补；Edge TTS 不用</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>发给接口的模型 id；Edge TTS 不用</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>默认音色；空 = 服务端默认（Edge TTS 为界面语言的一个常用音色）</summary>
    public string Voice { get; set; } = string.Empty;

    /// <summary>合成时要的音频格式（mp3 / wav）</summary>
    public string Format { get; set; } = "mp3";

    /// <summary>单次请求超时（秒）</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>加密后的 ApiKey，序列化到配置文件时使用（照 ImageModelInfo）</summary>
    [JsonPropertyName("ApiKey")]
    public string EncryptedApiKey { get; set; } = string.Empty;

    /// <summary>运行时要用的明文密钥（内存里才有）；新建时从同家对话模型复制</summary>
    [JsonIgnore]
    public string ApiKey
    {
        get => AesEncryptionUtils.DecryptString(EncryptedApiKey);
        set => EncryptedApiKey = AesEncryptionUtils.EncryptString(value);
    }

    /// <summary>在「用哪个」设置里的 id</summary>
    [JsonIgnore]
    public string Id => IdPrefix + Name;

    /// <summary>
    /// 听语气描述：OpenAI 只有 gpt-4o 系的合成模型认 instructions（tts-1 不认），别家兼容服务说不准就不发
    /// </summary>
    [JsonIgnore]
    public bool AcceptsInstructions => Dialect == EAudioDialect.OpenAICompatible && Task == EAudioCapability.Tts &&
                                       ModelId.StartsWith("gpt-4o", StringComparison.OrdinalIgnoreCase);

    /// <summary>能用了：Edge TTS 什么都不用填；兼容接口要地址与模型 id，密钥可空（自托管的常常不鉴权）</summary>
    [JsonIgnore]
    public bool IsConfigured => Dialect == EAudioDialect.EdgeTts ||
                                (!string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ModelId));

    /// <summary>
    /// 复制一份（编辑表单改副本，确认才整项替换）
    /// </summary>
    /// <returns>副本</returns>
    public AudioServiceInfo Clone() => new()
    {
        Name = Name, Dialect = Dialect, Task = Task, Endpoint = Endpoint, ModelId = ModelId, Voice = Voice,
        Format = Format, TimeoutSeconds = TimeoutSeconds, EncryptedApiKey = EncryptedApiKey
    };
}

/// <summary>
/// 远程音频服务列表。改动走 <see cref="ReplaceServices"/>，不要就地改（照 ImageModelSettingConfig）。
/// </summary>
public sealed class AudioServiceSettingConfig : TConfigBase<AudioServiceSettingConfig>
{
    /// <summary>远程音频服务</summary>
    public List<AudioServiceInfo> Services { get; set; } = new();

    /// <summary>列表换了一份（增删改）</summary>
    public event Action? ServicesChanged;

    /// <summary>整体换上一份新列表并落盘</summary>
    public void ReplaceServices(List<AudioServiceInfo> services)
    {
        Services = services;
        Save();
        ServicesChanged?.Invoke();
    }
}
