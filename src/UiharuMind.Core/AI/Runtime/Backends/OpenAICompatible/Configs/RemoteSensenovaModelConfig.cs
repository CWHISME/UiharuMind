using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

[SettingConfigDesc("Sensenova")]
[SettingConfigDesc("商汤科技(Sensenova)", LanguageUtils.ChineseSimplified)]
public class RemoteSensenovaModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    public override string ModelName { get; set; } = "Deepseek-V4-Flash";

    public override string ModelPath { get; set; } = "https://token.sensenova.cn/v1/chat/completions";

    public override string WebsiteUrl { get; set; } = "https://platform.sensenova.cn";

    /// <summary>
    /// 商汤的 /v1/models 目录带能力字段（input_modalities / max_output_length /
    /// supported_sampling_parameters），拉下来直接预填视觉、输出预算与采样参数。
    /// </summary>
    public override string ModelCatalogUrl { get; set; } = "https://token.sensenova.cn/v1/models";

    public override string ModelDescription { get; set; } = "";

    public override string ModelId { get; set; } = "deepseek-v4-flash";

    public override IReadOnlyDictionary<string, RemoteModelIdVariant> ModelIdVariants { get; } =
        new Dictionary<string, RemoteModelIdVariant>
        {
            // 商汤平台转售的第三方模型,ID 与参数沿用平台口径。
            // DeepSeek 的「必须含 reasoning_content」由基类按模型 ID 自动判定;roundtrip 默认 auto。
            ["deepseek-v4-flash"] = new(ContextLength: 1048576, MaxTokens: 327680),
            ["deepseek-flash"] = new(ContextLength: 1048576, MaxTokens: 393216, IsVision: true),
            ["sensenova-6.8-flash-lite"] = new(ContextLength: 262144, MaxTokens: 65535, IsVision: true,
                ReplayExistingReasoning: false, ThinkingField: "reasoning"),
        };

    public override int Port { get; set; }

    /// <summary>商汤转售的 GLM/DeepSeek 系走 <c>thinking.type</c>；与改动前的基类写法一致</summary>
    protected override EThinkingFormat DefaultThinkingFormat => EThinkingFormat.ThinkingType;
}
