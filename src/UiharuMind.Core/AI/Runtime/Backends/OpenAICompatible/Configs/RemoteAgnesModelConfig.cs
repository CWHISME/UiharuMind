using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// Agnes AI(OpenAI 兼容)。思考只有开/关两档(官方文档的
/// <c>chat_template_kwargs.enable_thinking</c> 布尔开关),没有力度分级;
/// Light/Medium/High/Max 四档都会收敛到开,None 为关,Default 不干预只给预算。
/// 基类的顶层 <c>thinking</c>/<c>reasoning_effort</c> 是 Anthropic/智谱那套写法,
/// 经 litellm 网关直接 400(<c>openai does not support parameters: ['thinking']</c>),这里不能用。
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

    /// <summary>
    /// Agnes 的思考开关。2.5/3.0 默认都是关,只有显式开才思考;
    /// 四个力度档在服务端没有对应分级,一律收敛为开。
    /// </summary>
    /// <returns>只含 <c>chat_template_kwargs</c> 与 <c>max_tokens</c>,永不含顶层 thinking</returns>
    public override IReadOnlyList<KeyValuePair<string, JsonNode?>>? GetExtraParams()
    {
        int maxTokens = MaxTokens > 0
            ? MaxTokens
            : (ModelIdVariants.TryGetValue(ModelId, out RemoteModelIdVariant? preset) && preset.MaxTokens > 0
                ? preset.MaxTokens
                : GetDefaultMaxTokens(ThinkingMode));

        KeyValuePair<string, JsonNode?>? thinkingParam = ThinkingMode switch
        {
            EThinkingMode.Default => null,
            EThinkingMode.None => new("chat_template_kwargs",
                new JsonObject { ["enable_thinking"] = JsonValue.Create(false) }),
            _ => new("chat_template_kwargs",
                new JsonObject { ["enable_thinking"] = JsonValue.Create(true) }),
        };

        if (thinkingParam.HasValue) return [thinkingParam.Value, new("max_tokens", JsonValue.Create(maxTokens))];
        return [new("max_tokens", JsonValue.Create(maxTokens))];
    }
}
