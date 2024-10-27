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
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

[DisplayName("Specific Config")]
public class LLamaCppServerSpecialConfig : ConfigBase
{
    [SettingConfigDesc("disable context shift on inifinite text generation (default: disabled)")]
    [SettingConfigNoneValue]
    public bool NoContextShift { get; set; } = false;

    [SettingConfigDesc("special tokens output enabled (default: false)")]
    [SettingConfigNoneValue]
    public bool Special { get; set; } = false;

    [SettingConfigDesc(
        "use Suffix/Prefix/Middle pattern for infill (instead of Prefix/Suffix/Middle) as some models prefer this. (default: disabled)")]
    [SettingConfigNoneValue]
    public bool SpmInfill { get; set; } = false;

    [SettingConfigDesc("pooling type for embeddings, use model default if unspecified (default: none)")]
    [SettingConfigOptions("none", "mean", "cls", "last")]
    public string Pooling { get; set; } = "none";

    // [SettingConfigDesc("enable continuous batching (a.k.a dynamic batching) (default: enabled)")]
    // [SettingConfigNoneValue]
    // public bool ContBatching { get; set; } = true;

    [SettingConfigDesc("disable continuous batching (default: disabled)")]
    [SettingConfigNoneValue]
    public bool NoContBatching { get; set; } = false;

    // [SettingConfigDesc("restrict to only support embedding use case; use only with dedicated embedding models (default: disabled)")]
    // public bool Embeddings { get; set; } = false;

    // [SettingConfigDesc("API key to use for authentication (default: none)")]
    // public string ApiKey { get; set; } = "";

    // [SettingConfigDesc("path to file containing API keys (default: none)")]
    // public string ApiKeyFile { get; set; } = "";

    [SettingConfigDesc("server read/write timeout in seconds (default: 600)")]
    public int Timeout { get; set; } = 600;

    // [SettingConfigDesc("number of threads used to process HTTP requests (default: -1)")]
    // public int ThreadsHttp { get; set; } = -1;

    // [SettingConfigDesc("set a file to load a system prompt (initial prompt of all slots), this is useful for chat applications (default: none)")]
    // public string SystemPromptFile { get; set; } = "";

    [SettingConfigDesc("enable prometheus compatible metrics endpoint (default: disabled)")]
    //Prometheus 是一个开源的监控和报警工具，通常用于收集和存储时间序列数据
    [SettingConfigNoneValue]
    public bool Metrics { get; set; } = false;

    [SettingConfigDesc("disable slots monitoring endpoint (default: enabled)")]
    //这个端点通常用于监控模型运行时的资源使用情况
    [SettingConfigNoneValue]
    public bool NoSlots { get; set; } = true;

    [SettingConfigDesc("path to save slot kv cache (default: disabled)")]
    public string SlotSavePath { get; set; } = "";

    [SettingConfigDesc(
        "how much the prompt of a request must match the prompt of a slot in order to use that slot (default: 0.50, 0.0 = disabled)")]
    public float SlotPromptSimilarity { get; set; } = 0.5f;
}