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
    public const string BackendOpenAICompatible = "OpenAICompatible";

    public const string SourceModeLocal = "Local";
    public const string SourceModeManagedLocal = "ManagedLocal";
    public const string SourceModeCustomLocal = "CustomLocal";
    public const string SourceModeRemoteApi = "RemoteApi";

    /// <summary>
    /// 嵌入模型来源
    /// </summary>
    public string SourceMode { get; set; } = SourceModeLocal;

    /// <summary>
    /// 嵌入模型后端：远程为 OpenAICompatible；本地留空，跟随「本地引擎」设置
    /// </summary>
    public string Backend { get; set; } = "";

    /// <summary>
    /// 嵌入模型路径。留空时使用模型目录里找到的第一个嵌入模型。
    /// </summary>
    public string ModelPath { get; set; } = "";

    /// <summary>
    /// 首次需要时加载嵌入模型，并常驻内存。
    /// </summary>
    public bool KeepLoaded { get; set; } = true;

    /// <summary>
    /// 嵌入上下文长度
    /// </summary>
    public int ContextSize { get; set; } = 8192;

    /// <summary>
    /// 嵌入逻辑批处理大小
    /// </summary>
    public int BatchSize { get; set; } = 8192;

    /// <summary>
    /// 嵌入物理批处理大小
    /// </summary>
    public int UBatchSize { get; set; } = 8192;

    /// <summary>
    /// 嵌入模型 GPU 层数（-1 表示尽可能全部使用）。
    /// </summary>
    public int GpuLayers { get; set; } = 0;

    /// <summary>
    /// OpenAI 兼容嵌入接口地址，供远程嵌入后端使用。
    /// </summary>
    public string RemoteEndpoint { get; set; } = "";

    /// <summary>
    /// OpenAI 兼容嵌入模型 ID，供远程嵌入后端使用。
    /// </summary>
    public string RemoteModelId { get; set; } = "";

    /// <summary>
    /// OpenAI 兼容嵌入 API Key，供远程嵌入后端使用。
    /// </summary>
    public string RemoteApiKey { get; set; } = "";
}