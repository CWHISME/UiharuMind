using System.ComponentModel;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

[DisplayName("Sampling Strategies")]
public class LLamaCppSamplingStrategiesConfig : ConfigBase
{
    [SettingConfigDesc("penalize newline tokens (default: false)")]
    public bool PenalizeNl { get; set; }

    [SettingConfigDesc("temperature for sampling (default: 0.8)")]
    public float Temp { get; set; } = 0.8f;

    [SettingConfigDesc("top-k sampling (default: 40, 0 = disabled)")]
    public int TopK { get; set; } = 40;

    [SettingConfigDesc("top-p sampling (default: 0.9, 1.0 = disabled)")]
    public float TopP { get; set; } = 0.9f;

    [SettingConfigDesc("min-p sampling (default: 0.1, 0.0 = disabled)")]
    public float MinP { get; set; } = 1.0f;

    [SettingConfigDesc("tail free sampling, parameter z (default: 1.0, 1.0 = disabled)")]
    public float Tfs { get; set; } = 1.0f;

    [SettingConfigDesc("locally typical sampling, parameter p (default: 1.0, 1.0 = disabled)")]
    public float Typical { get; set; } = 1.0f;

    [SettingConfigDesc("last n tokens to consider for penalize (default: 64, 0 = disabled, -1 = ctx_size)")]
    public int RepeatLastN { get; set; } = 64;

    [SettingConfigDesc("penalize repeat sequence of tokens (default: 1.0, 1.0 = disabled)")]
    public float RepeatPenalty { get; set; } = 1.0f;

    [SettingConfigDesc("repeat alpha presence penalty (default: 0.0, 0.0 = disabled)")]
    public float PresencePenalty { get; set; } = 0.0f;

    [SettingConfigDesc("repeat alpha frequency penalty (default: 0.0, 0.0 = disabled)")]
    public float FrequencyPenalty { get; set; } = 0.0f;

    [SettingConfigDesc("dynamic temperature range (default: 0.0, 0.0 = disabled)")]
    public float DynatempRange { get; set; } = 0.0f;

    [SettingConfigDesc("dynamic temperature exponent (default: 1.0)")]
    public float DynatempExp { get; set; } = 1.0f;

    [SettingConfigDesc(
        "use Mirostat sampling. Top K, Nucleus, Tail Free and Locally Typical samplers are ignored if used. (default: 0, 0 = disabled, 1 = Mirostat, 2 = Mirostat 2.0)")]
    public int Mirostat { get; set; } = 0;

    [SettingConfigDesc("Mirostat learning rate, parameter eta (default: 0.1)")]
    public float MirostatLr { get; set; } = 0.1f;

    [SettingConfigDesc("Mirostat target entropy, parameter tau (default: 5.0)")]
    public float MirostatEnt { get; set; } = 5.0f;

    [SettingConfigDesc("negative prompt to use for guidance (default: '')")]
    public string CfgNegativePrompt { get; set; } = "";

    [SettingConfigDesc("strength of guidance (default: 1.0, 1.0 = disable)")]
    public float CfgScale { get; set; } = 1.0f;

    // [SettingConfigDesc(
    //     "set custom jinja chat template (default: template taken from model's metadata) if suffix/prefix are specified, template will be disabled only commonly used templates are accepted: https://github.com/ggerganov/llama.cpp/wiki/Templates-supported-by-llama_chat_apply_template")]
    // public string ChatTemplate { get; set; } = "";
}