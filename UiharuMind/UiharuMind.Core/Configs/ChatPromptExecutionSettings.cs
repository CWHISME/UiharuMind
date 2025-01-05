using System.Text.Json.Serialization;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs;

public class ChatPromptExecutionSettings : ConfigBase
{
    [SettingConfigDesc(
        "Temperature controls the randomness of the completion. The higher the temperature, the more random the completion. Default is 1.0.")]
    [SettingConfigDesc(
        "控制生成文本的随机性:\n较高的温度值会使模型生成更加多样化和随机的结果，而较低的温度值会使生成的结果更加确定和集中。\n通常在0到1之间，但也可以大于1。默认为1.0。\n适用场景:\n\t高温度：适用于需要创造性和多样性的任务，如创意写作。\n\t低温度：适用于需要准确性和一致性的任务，如翻译或问答。",
        LanguageUtils.ChineseSimplified)]
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; } = 1.0;

    [SettingConfigDesc(
        "TopP controls the diversity of the completion. The higher the TopP, the more diverse the completion. Default is 1.0.")]
    [SettingConfigDesc(
        "影响生成文本的多样性:\n模型只从概率最高的 token 中选择，直到这些 token 的概率之和达到 TopP 的值。简单来说，TopP 值越高，生成的文本越多样化。\n通常在0到1之间。默认为1.0。\n适用场景:\n\t高TopP：适用于需要多样性和创造性的任务。\n\t低TopP：适用于需要准确性和一致性的任务。",
        LanguageUtils.ChineseSimplified)]
    [JsonPropertyName("top_p")]
    public double? TopP { get; set; } = 1.0;

    [SettingConfigDesc(
        "Number between -2.0 and 2.0. Positive values penalize new tokens based on their existing frequency in the text so far, decreasing the model's likelihood to repeat the same line verbatim.")]
    [SettingConfigDesc("主要影响生成文本中重复token的概率:\n较高的频率惩罚值会减少模型生成重复token的概率。\n通常在0到2之间。默认为0。",
        LanguageUtils.ChineseSimplified)]
    [SettingConfigRange(-2.0f, 2.0f, 0.01f)]
    [JsonPropertyName("presence_penalty")]
    public double? PresencePenalty { get; set; }

    [SettingConfigDesc(
        "Number between -2.0 and 2.0. Positive values penalize new tokens based on whether they appear in the text so far, increasing the model's likelihood to talk about new topics..")]
    [SettingConfigDesc("主要影响生成文本中引入新 token 的概率:\n较高的存在惩罚值会减少模型生成新 token 的概率，即使这些 token 在之前的文本中没有出现过。\n通常在0到2之间。默认为0。",
        LanguageUtils.ChineseSimplified)]
    [SettingConfigRange(-2.0f, 2.0f, 0.01f)]
    [JsonPropertyName("frequency_penalty")]
    public double? FrequencyPenalty { get; set; }
}