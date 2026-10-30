using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// OpenRouter（聚合网关，OpenAI 兼容）。候选走运行时模型目录（ADR 0092）。
///
/// 它是目前<b>唯一认会话亲和头</b>的供应商：按 <c>x-session-id</c> 把同一会话的请求钉到同一台上游副本，
/// 前缀缓存按副本各存一份，落错副本就是整段冷启动。其余供应商都不认，因此
/// <see cref="BaseRemoteModelConfig.SessionAffinityHeaders"/> 默认空，只有这里声明。
/// </summary>
[SettingConfigDesc("OpenRouter")]
[SettingConfigDesc("OpenRouter", LanguageUtils.ChineseSimplified)]
public class RemoteOpenRouterModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    public override string ModelName { get; set; } = "OpenRouter";

    public override string ModelPath { get; set; } = "https://openrouter.ai/api/v1/chat/completions";

    public override string WebsiteUrl { get; set; } = "https://openrouter.ai";

    public override string ModelCatalogUrl { get; set; } = "https://openrouter.ai/api/v1/models";

    public override string ModelDescription { get; set; } = "";

    /// <summary>无默认模型：目录拉下来让用户挑</summary>
    public override string ModelId { get; set; } = "";

    public override int Port { get; set; }

    /// <summary>只认这一条；多发（<c>session_id</c> / <c>x-client-request-id</c>）它都不看，只是噪音</summary>
    public override IReadOnlyList<string>? SessionAffinityHeaders => ["x-session-id"];

    /// <summary>OpenRouter 认 body 级的 <c>prompt_cache_key</c>（与亲和头是两条独立的路，一起发）</summary>
    public override bool SupportsPromptCacheKey => true;
}
