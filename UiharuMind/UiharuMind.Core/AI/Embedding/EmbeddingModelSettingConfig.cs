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

using System.ComponentModel;
using System.Text.Json.Serialization;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Embedding;

[DisplayName("Embedding Model")]
public class EmbeddingModelSettingConfig : TConfigBase<EmbeddingModelSettingConfig>
{
    public const string SourceModeLocal = "Local";
    public const string SourceModeManagedLocal = "ManagedLocal";
    public const string SourceModeCustomLocal = "CustomLocal";
    public const string SourceModeRemoteApi = "RemoteApi";

    //内置模型目录路径(不可修改)
    [JsonIgnore] public string DefaultEmbededModelPath { get; set; } = "./InternalEmbededModels";

    //外部模型目录路径(可修改)
    public string ExternalEmbededModelPath { get; set; } = Path.Combine(SettingConfig.RootDataPath, "EmbededModels");

    [SettingConfigDesc("Embedding model source")]
    [SettingConfigDesc("嵌入模型来源", LanguageUtils.ChineseSimplified)]
    [SettingConfigOptions(SourceModeLocal, SourceModeRemoteApi)]
    public string SourceMode { get; set; } = SourceModeLocal;

    [SettingConfigDesc("Embedding backend")]
    [SettingConfigDesc("嵌入模型后端", LanguageUtils.ChineseSimplified)]
    [SettingConfigOptions("LLamaSharp", "OpenAICompatible")]
    public string Backend { get; set; } = "LLamaSharp";

    [SettingConfigDesc("Embedding model path. Empty means using the first GGUF file in the embedding model folders.")]
    [SettingConfigDesc("嵌入模型路径。留空时会使用嵌入模型目录中的第一个 GGUF 文件。", LanguageUtils.ChineseSimplified)]
    public string ModelPath { get; set; } = "";

    [SettingConfigDesc("Preload embedding model when it is first needed and keep it in memory.")]
    [SettingConfigDesc("首次需要时加载嵌入模型，并常驻内存。", LanguageUtils.ChineseSimplified)]
    [SettingConfigNoneValue]
    public bool KeepLoaded { get; set; } = true;

    [SettingConfigDesc("embedding context size")]
    [SettingConfigDesc("嵌入上下文长度", LanguageUtils.ChineseSimplified)]
    public int ContextSize { get; set; } = 8192;

    [SettingConfigDesc("embedding logical batch size")]
    [SettingConfigDesc("嵌入逻辑批处理大小", LanguageUtils.ChineseSimplified)]
    public int BatchSize { get; set; } = 8192;

    [SettingConfigDesc("embedding physical batch size")]
    [SettingConfigDesc("嵌入物理批处理大小", LanguageUtils.ChineseSimplified)]
    public int UBatchSize { get; set; } = 8192;

    [SettingConfigDesc("GPU layers for embedding model (-1 means all possible layers).")]
    [SettingConfigDesc("嵌入模型 GPU 层数（-1 表示尽可能全部使用）。", LanguageUtils.ChineseSimplified)]
    public int GpuLayers { get; set; } = 0;

    [SettingConfigDesc("OpenAI compatible embedding endpoint. Reserved for remote embedding backend.")]
    [SettingConfigDesc("OpenAI 兼容嵌入接口地址，供远程嵌入后端使用。", LanguageUtils.ChineseSimplified)]
    public string RemoteEndpoint { get; set; } = "";

    [SettingConfigDesc("OpenAI compatible embedding model id. Reserved for remote embedding backend.")]
    [SettingConfigDesc("OpenAI 兼容嵌入模型 ID，供远程嵌入后端使用。", LanguageUtils.ChineseSimplified)]
    public string RemoteModelId { get; set; } = "";

    [SettingConfigDesc("OpenAI compatible embedding API key. Reserved for remote embedding backend.")]
    [SettingConfigDesc("OpenAI 兼容嵌入 API Key，供远程嵌入后端使用。", LanguageUtils.ChineseSimplified)]
    public string RemoteApiKey { get; set; } = "";
}