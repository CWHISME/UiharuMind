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

[DisplayName("General Config")]
public class LLamaCppServerGeneralConfig : ConfigBase
{
    [SettingConfigDesc("number of layers to store in VRAM")]
    public int GpuLayers { get; set; } = 0;

    [SettingConfigDesc("force system to keep model in RAM rather than swapping or compressing")]
    [SettingConfigNoneValue]
    [DefaultValue(false)]
    public bool Mlock { get; set; } = true;

    [SettingConfigDesc("do not memory-map model (slower load but may reduce pageouts if not using mlock)")]
    public bool NoMmap { get; set; } = false;

    [SettingConfigDesc("enable Flash Attention (default: disabled)")]
    public bool FlashAttn { get; set; } = false;

    [SettingConfigDesc(
        "the GPU to use for the model (with split-mode = none), or for intermediate results and KV (with split-mode = row) (default: 0)")]
    public int MainGpu { get; set; } = 0;

    [SettingConfigDesc("check model tensor data for invalid values (default: false)")]
    public bool CheckTensors { get; set; } = false;

    [SettingConfigDesc("how to split the model across multiple GPUs")]
    [SettingConfigOptions("none", "layer", "row")]
    public string SplitMode { get; set; } = "none";

    // [SettingConfigDesc("fraction of the model to offload to each GPU, comma-separated list of proportions, e.g. 3,1")]
    // public string TensorSplit { get; set; } = "";

    // [SettingConfigDesc(
    //     "set custom jinja chat template (default: template taken from model's metadata) if suffix/prefix are specified, template will be disabled only commonly used templates are accepted: https://github.com/ggerganov/llama.cpp/wiki/Templates-supported-by-llama_chat_apply_template (default: none)")]
    // [SettingConfigCustomOptions("chatml", "llama2", "monarch", "gemma", "orion", "openchat", "vicuna", "vicuna-orca",
    //     "deepseek", "command-r", "llama3", "zephyr")]
    // public string ChatTemplate { get; set; } = "";
}