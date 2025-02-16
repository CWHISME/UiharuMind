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
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

[DisplayName("Running Params Config")]
public class LLamaCppServerParamsConfig : ConfigBase
{
    [SettingConfigDesc("size of the prompt context (default: 0, 0 = loaded from model)")]
    public int CtxSize { get; set; } = 4096;

    [SettingConfigDesc("number of tokens to predict (default: -1, -1 = infinity, -2 = until context filled)")]
    public int Predict { get; set; } = -1;

    [SettingConfigDesc("logical maximum batch size (default: 2048)")]
    public int BatchSize { get; set; } = 2048;

    [SettingConfigDesc("physical maximum batch size (default: 512)")]
    public int UbatchSize { get; set; } = 512;

    [SettingConfigDesc("number of tokens to keep from the initial prompt (default: 0, -1 = all)")]
    public int Keep { get; set; } = 0;

    [SettingConfigDesc("prompt to start generation with")]
    public string Prompt { get; set; } = "";

    [SettingConfigDesc("disable internal libllama performance timings (default: false)")]
    [SettingConfigNoneValue]
    public bool NoPerf { get; set; } = false;

    // [SettingConfigDesc(@"process escapes sequences (\n, \r, \t, \', \, \\) (default: true)")]
    // public bool Escape { get; set; } = true;

    [SettingConfigDesc("do not process escape sequences (default: false)")]
    [SettingConfigNoneValue]
    public bool NoEscape { get; set; } = false;

    [SettingConfigDesc("group-attention factor (default: 1)")]
    public int GrpAttnN { get; set; } = 1;

    [SettingConfigDesc("group-attention width (default: 512.0)")]
    public float GrpAttnW { get; set; } = 512.0f;

    [SettingConfigDesc("verbose print of the KV cache")]
    [SettingConfigNoneValue]
    public bool DumpKvCache { get; set; } = false;

    [SettingConfigDesc("disable KV offload")]
    [SettingConfigNoneValue]
    public bool NoKvOffload { get; set; } = false;

    [SettingConfigDesc("KV cache data type for K (default: f16)")]
    [SettingConfigOptions("int8", "f16", "f32")]
    public string CacheTypeK { get; set; } = "f16";

    [SettingConfigDesc("KV cache data type for V (default: f16)")]
    [SettingConfigOptions("int8", "f16", "f32")]
    public string CacheTypeV { get; set; } = "f16";

    [SettingConfigDesc("KV cache defragmentation threshold (default: -1.0, < 0 - disabled)")]
    [SettingConfigDesc("控制内存碎片整理的阈值。当内存碎片达到或超过这个阈值时，系统会触发内存碎片整理操作。(default: -1.0, < 0 - disabled)", LanguageUtils.ChineseSimplified)]
    [SettingConfigRange(-1.0f, 1.0f, 0.01f)]
    public float DefragThold { get; set; } = -1.0f;

    [SettingConfigDesc("number of parallel sequences to decode (default: 1)")]
    public int Parallel { get; set; } = 1;
}