/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using System.Text.Json.Serialization;
using System.ComponentModel;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs;

[DisplayName("Model Runtime")]
public class ModelRuntimeSettingConfig : TConfigBase<ModelRuntimeSettingConfig>
{
    public const string LLamaSharpBackendAuto = "Auto";
    public const string LLamaSharpBackendCpu = "CPU";
    public const string LLamaSharpBackendGpu = "GPU";

    /// <summary>
    /// 首选的本地引擎 Id，空或未注册时用第一个能跑的。沿用旧字段名 EngineType，旧值本就是引擎 Id
    /// </summary>
    [JsonPropertyName("EngineType")]
    public string LocalEngineId { get; set; } = "";

    // LLamaSharp 已屏蔽（ADR 0067），只剩其死代码还在读
    [SettingConfigDesc("LLamaSharp backend mode")]
    [SettingConfigDesc("LLamaSharp 后端模式", LanguageUtils.ChineseSimplified)]
    [SettingConfigOptions(LLamaSharpBackendAuto, LLamaSharpBackendCpu, LLamaSharpBackendGpu)]
    public string LLamaSharpBackendMode { get; set; } = LLamaSharpBackendAuto;

    [SettingConfigDesc("Context size")]
    [SettingConfigDesc("上下文长度", LanguageUtils.ChineseSimplified)]
    public int ContextSize { get; set; } = 0;

    [SettingConfigDesc("GPU layers")]
    [SettingConfigDesc("GPU 层数", LanguageUtils.ChineseSimplified)]
    public int GpuLayers { get; set; } = 0;

    [SettingConfigDesc("Logical batch size")]
    [SettingConfigDesc("逻辑批处理大小", LanguageUtils.ChineseSimplified)]
    public int BatchSize { get; set; } = 0;

    [SettingConfigDesc("Physical batch size")]
    [SettingConfigDesc("物理批处理大小", LanguageUtils.ChineseSimplified)]
    public int UBatchSize { get; set; } = 0;

    [SettingConfigDesc("CPU threads. 0 means auto.")]
    [SettingConfigDesc("CPU 线程数，0 表示自动。", LanguageUtils.ChineseSimplified)]
    public int Threads { get; set; } = 0;

    [SettingConfigDesc("Enable Flash Attention")]
    [SettingConfigDesc("启用 Flash Attention", LanguageUtils.ChineseSimplified)]
    [SettingConfigNoneValue]
    public bool FlashAttention { get; set; } = false;
}
