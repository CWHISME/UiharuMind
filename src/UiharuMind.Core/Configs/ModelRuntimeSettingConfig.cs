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

    /// <summary>
    /// 卸载到 GPU 的层数：负数自动（交给 llama-server 按显存放），0 纯 CPU。
    /// 换了 JSON 名：旧字段默认 0 等于强制纯 CPU，Metal/显卡都闲着，借换名让所有人回到自动
    /// </summary>
    [SettingConfigDesc("GPU layers. -1 means auto, 0 means CPU only.")]
    [SettingConfigDesc("GPU 层数，-1 表示自动，0 表示纯 CPU。", LanguageUtils.ChineseSimplified)]
    [JsonPropertyName("GpuOffloadLayers")]
    public int GpuLayers { get; set; } = -1;

    [SettingConfigDesc("Logical batch size")]
    [SettingConfigDesc("逻辑批处理大小", LanguageUtils.ChineseSimplified)]
    public int BatchSize { get; set; } = 0;

    [SettingConfigDesc("Physical batch size")]
    [SettingConfigDesc("物理批处理大小", LanguageUtils.ChineseSimplified)]
    public int UBatchSize { get; set; } = 0;

    [SettingConfigDesc("CPU threads. 0 means auto.")]
    [SettingConfigDesc("CPU 线程数，0 表示自动。", LanguageUtils.ChineseSimplified)]
    public int Threads { get; set; } = 0;

    /// <summary>
    /// Flash Attention：null 自动（llama-server 默认），true 开，false 关。
    /// 旧字段是 bool 且 false 时本来就不传参数（即自动），换 JSON 名免得旧的 false 被读成「关」
    /// </summary>
    [SettingConfigDesc("Flash Attention. Empty means auto.")]
    [SettingConfigDesc("Flash Attention，留空表示自动。", LanguageUtils.ChineseSimplified)]
    [JsonPropertyName("FlashAttn")]
    public bool? FlashAttention { get; set; }

    /// <summary>
    /// 各本地模型自己的运行参数，按模型名
    /// </summary>
    [SettingConfigIgnoreDisplay]
    public Dictionary<string, ModelRuntimeOverrides> ModelOverrides { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 取某个模型自己的运行参数
    /// </summary>
    /// <param name="modelName">模型名</param>
    /// <returns>覆写；全部跟随全局为 null</returns>
    public ModelRuntimeOverrides? GetOverrides(string modelName) =>
        ModelOverrides.TryGetValue(modelName, out ModelRuntimeOverrides? overrides) && !overrides.IsEmpty ? overrides : null;

    /// <summary>
    /// 设置某个模型自己的运行参数，全部跟随全局时移除（不落盘，调用方 Save）
    /// </summary>
    /// <param name="modelName">模型名</param>
    /// <param name="overrides">覆写；null 移除</param>
    public void SetOverrides(string modelName, ModelRuntimeOverrides? overrides)
    {
        if (overrides == null || overrides.IsEmpty) ModelOverrides.Remove(modelName);
        else ModelOverrides[modelName] = overrides;
    }

    /// <summary>
    /// 某个模型实际生效的运行参数：全局叠上它自己的覆写。没有覆写时返回自身
    /// </summary>
    /// <param name="modelName">模型名</param>
    /// <returns>生效的设置（有覆写时是副本，别拿去落盘）</returns>
    public ModelRuntimeSettingConfig ForModel(string modelName)
    {
        if (GetOverrides(modelName) is not { } overrides) return this;
        return new ModelRuntimeSettingConfig
        {
            LocalEngineId = LocalEngineId,
            LLamaSharpBackendMode = LLamaSharpBackendMode,
            ContextSize = overrides.ContextSize ?? ContextSize,
            GpuLayers = overrides.GpuLayers ?? GpuLayers,
            BatchSize = overrides.BatchSize ?? BatchSize,
            UBatchSize = overrides.UBatchSize ?? UBatchSize,
            Threads = overrides.Threads ?? Threads,
            FlashAttention = FlashAttention
        };
    }
}
