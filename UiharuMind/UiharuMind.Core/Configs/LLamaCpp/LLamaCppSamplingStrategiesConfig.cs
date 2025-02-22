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

[DisplayName("Sampling Strategies")]
public class LLamaCppSamplingStrategiesConfig : ConfigBase
{
    
    [SettingConfigDesc("ignore end of stream token and continue generating (implies --logit-bias EOS-inf)")]
    [SettingConfigDesc("主要作用是控制是否忽略 EOS 标记，通过忽略 EOS 标记，可以生成更长的文本，但也可能会导致生成文本的质量下降。",
        LanguageUtils.ChineseSimplified)]
    [SettingConfigNoneValue]
    public bool IgnoreEos { get; set; }
    
    [SettingConfigDesc("penalize newline tokens (default: false)")]
    [SettingConfigDesc("用于控制是否对换行符（newline tokens）进行惩罚。换行符通常表示文本中的段落或句子分隔，而在某些情况下可能希望减少生成文本中的换行符数量。",
        LanguageUtils.ChineseSimplified)]
    [SettingConfigNoneValue]
    public bool PenalizeNl { get; set; }

    [SettingConfigDesc("temperature for sampling (default: 0.8)")]
    [SettingConfigDesc(
        "用于控制生成文本的随机性。温度值越高，生成文本的随机性越大，多样性越高；温度值越低，生成文本的随机性越小，更加确定性。温度值通常在 0 到 1 之间，但也可以设置为大于 1 的值以增加随机性",
        LanguageUtils.ChineseSimplified)]
    [SettingConfigRange(0f, 2.0f, 0.01f)]
    public float Temp { get; set; } = 0.8f;

    [SettingConfigDesc("top-k sampling (default: 40, 0 = disabled)")]
    //限制模型在每个生成步骤中只考虑概率最高的 k 个词
    [SettingConfigDesc("用于控制模型在每个生成步骤中只考虑概率最高的 k 个词。较小的 k 值会生成更加一致的文本，较大的 k 值会生成更加多样化的文本。",
        LanguageUtils.ChineseSimplified)]
    public int TopK { get; set; } = 40;

    [SettingConfigDesc("top-p sampling (default: 0.9, 1.0 = disabled)")]
    //限制模型在每个生成步骤中只考虑累积概率达到某个阈值 p 的词
    [SettingConfigDesc("用于控制模型在每个生成步骤中只考虑累积概率达到某个阈值 p 的词。可以在一定程度上控制生成文本的多样性。较小的 p 值会生成更加一致的文本，较大的 p 值会生成更加多样化的文本。",
        LanguageUtils.ChineseSimplified)]
    [SettingConfigRange(0f, 1.0f, 0.01f)]
    public float TopP { get; set; } = 0.9f;

    [SettingConfigDesc("min-p sampling (default: 0.1, 0.0 = disabled)")]
    [SettingConfigDesc(
        "用于控制生成文本时的最小概率阈值（Minimum Probability）。MinP 参数通常与 top-p 采样结合使用，以进一步限制生成文本中的低概率词。较小的 MinP 值会生成更加一致的文本，较大的 MinP 值会生成更加多样化的文本。",
        LanguageUtils.ChineseSimplified)]
    public float MinP { get; set; } = 1.0f;

    [SettingConfigDesc("tail free sampling, parameter z (default: 1.0, 1.0 = disabled)")]
    [SettingConfigDesc(
        "一种改进的采样方法，旨在减少生成文本中的低概率词，同时保留一定的多样性。通过去除概率分布的尾部词来提高生成文本的质量和一致性。parameter z 用于控制去除尾部词的程度，较大的 z 值会生成更加一致的文本，较小的 z 值会生成更加多样化的文本。",
        LanguageUtils.ChineseSimplified)]
    public float Tfs { get; set; } = 1.0f;

    [SettingConfigDesc("locally typical sampling, parameter p (default: 1.0, 1.0 = disabled)")]
    [SettingConfigDesc("通过选择与上下文更加一致的词来提高生成文本的质量和一致性。parameter p 用于控制采样的行为，较大的 p 值会生成更加一致的文本，较小的 p 值会生成更加多样化的文本。",
        LanguageUtils.ChineseSimplified)]
    public float Typical { get; set; } = 1.0f;

    [SettingConfigDesc("last n tokens to consider for penalize (default: 64, 0 = disabled, -1 = ctx_size)")]
    [SettingConfigDesc("控制生成文本时考虑的最近 n 个词的数量，并对这些词进行惩罚，以减少生成文本中的重复内容。", LanguageUtils.ChineseSimplified)]
    public int RepeatLastN { get; set; } = 64;

    [SettingConfigDesc("penalize repeat sequence of tokens (default: 1.0, 1.0 = disabled)")]
    [SettingConfigDesc(
        "控制生成文本时的重复惩罚，通过降低模型选择最近出现过的词的概率，减少生成文本中的重复内容。RepeatPenalty = 1：不进行任何惩罚，模型会考虑所有可能的词。\n\nRepeatPenalty > 1：生成文本具有一定的惩罚机制，模型会降低最近出现过的词的概率，从而减少生成文本中的重复内容。",
        LanguageUtils.ChineseSimplified)]
    public float RepeatPenalty { get; set; } = 1.0f;

    [SettingConfigDesc("repeat alpha presence penalty (default: 0.0, 0.0 = disabled)")]
    [SettingConfigDesc("控制生成文本时的存在惩罚，通过降低模型选择已经出现过的词的概率，减少生成文本中的重复内容。存在惩罚值通常在 0 到 1 之间，表示对已经出现过的词的惩罚程度。",
        LanguageUtils.ChineseSimplified)]
    public float PresencePenalty { get; set; } = 0.0f;

    [SettingConfigDesc("repeat alpha frequency penalty (default: 0.0, 0.0 = disabled)")]
    [SettingConfigDesc(
        "控制生成文本时的频率惩罚，通过降低模型选择高频词的概率，减少生成文本中的高频词频率。FrequencyPenalty 较大：生成文本的惩罚机制更强，模型会更大程度地降低高频词的概率，生成更加多样化的文本。",
        LanguageUtils.ChineseSimplified)]
    public float FrequencyPenalty { get; set; } = 0.0f;

    [SettingConfigDesc("dynamic temperature range (default: 0.0, 0.0 = disabled)")]
    [SettingConfigDesc(
        "控制生成文本时的动态温度范围，通过在生成文本的不同阶段动态调整温度参数，提高生成文本的质量和多样性。DynatempRange 较大：生成文本的温度参数变化范围更大，模型会在更大的范围内动态调整温度值，生成更加多样化的文本。",
        LanguageUtils.ChineseSimplified)]
    public float DynatempRange { get; set; } = 0.0f;

    [SettingConfigDesc("dynamic temperature exponent (default: 1.0)")]
    [SettingConfigDesc(
        "动态温度指数是一种改进的采样方法，旨在在生成文本时动态调整温度参数，以提高生成文本的质量和多样性。较大的 DynatempExp 值会生成更加多样化的文本，较小的 DynatempExp 值会生成更加一致的文本。",
        LanguageUtils.ChineseSimplified)]
    public float DynatempExp { get; set; } = 1.0f;

    [SettingConfigDesc(
        "use Mirostat sampling. Top K, Nucleus, Tail Free and Locally Typical samplers are ignored if used. (default: 0, 0 = disabled, 1 = Mirostat, 2 = Mirostat 2.0)")]
    [SettingConfigDesc(
        "Mirostat 采样策略允许模型在生成文本时根据上下文动态调整采样参数，从而在不同的生成阶段使用不同的采样策略。启用时，Top K, Nucleus, Tail Free 和 Locally Typical 采样策略会被忽略。(default: 0, 0 = disabled, 1 = Mirostat, 2 = Mirostat 2.0)",
        LanguageUtils.ChineseSimplified)]
    [SettingConfigOptions(new string[] { "0", "1", "2" })]
    public string Mirostat { get; set; } = "0";

    [SettingConfigDesc("Mirostat learning rate, parameter eta (default: 0.1)")]
    [SettingConfigDesc(
        "用于控制 Mirostat 采样策略的学习率。较大的学习率会导致模型更快地适应新的采样策略，从而生成更加多样化的文本。默认值为 0.1。",
        LanguageUtils.ChineseSimplified)]
    public float MirostatLr { get; set; } = 0.1f;

    [SettingConfigDesc("Mirostat target entropy, parameter tau (default: 5.0)")]
    [SettingConfigDesc("用于控制 Mirostat 采样策略的目标熵。较大的目标熵会导致模型更加关注生成文本的多样性，从而生成更加多样化的文本。默认为 5.0。",
        LanguageUtils.ChineseSimplified)]
    public float MirostatEnt { get; set; } = 5.0f;

    // [SettingConfigDesc("negative prompt to use for guidance (default: '')")]
    // [SettingConfigDesc(
    //     "负提示是一种改进的采样方法，旨在减少生成文本中不希望出现的内容。通过指定负提示，模型会在生成文本时尽量避免生成与负提示相关的内容。",
    //     LanguageUtils.ChineseSimplified)]
    // public string CfgNegativePrompt { get; set; } = "";
    //
    // [SettingConfigDesc("strength of guidance (default: 1.0, 1.0 = disable)")]
    // [SettingConfigDesc(
    //     "控制负提示的影响力。较大的影响力会导致模型更加关注负提示的内容，从而生成更加符合负提示的内容。",
    //     LanguageUtils.ChineseSimplified)]
    // public float CfgScale { get; set; } = 1.0f;
}