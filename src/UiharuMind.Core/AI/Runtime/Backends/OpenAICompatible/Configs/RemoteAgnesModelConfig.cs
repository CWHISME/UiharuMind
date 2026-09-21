using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// Agnes AI(OpenAI 兼容)。基类 Thinking 档位发出的 thinking/reasoning_effort 参数
/// 与 Agnes 的 chat_template_kwargs.enable_thinking 格式不一致,建议保持默认档位。
/// </summary>
[SettingConfigDesc("Agnes AI(CN)")]
[SettingConfigDesc("Agnes AI(CN)", LanguageUtils.ChineseSimplified)]
public class RemoteAgnesModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    public override string ModelName { get; set; } = "Agnes-3.0-Flash";

    public override string ModelPath { get; set; } = "https://api.agnes-ai.cn/v1/chat/completions";

    public override string WebsiteUrl { get; set; } = "https://wiki.agnes-ai.cn";

    public override string ModelDescription { get; set; } = "";

    public override string ModelId { get; set; } = "agnes-3.0-flash";

    public override IReadOnlyDictionary<string, RemoteModelIdVariant> ModelIdVariants { get; } =
        new Dictionary<string, RemoteModelIdVariant>
        {
            ["agnes-2.5-flash"] = new(ContextLength: 524288, MaxTokens: 65536, IsVision: true),
            ["agnes-2.5-pro"] = new(ContextLength: 1048576, MaxTokens: 65536, IsVision: true),
            ["agnes-3.0-flash"] = new(ContextLength: 524288, MaxTokens: 65536, IsVision: true),
        };

    public override int Port { get; set; }
}
