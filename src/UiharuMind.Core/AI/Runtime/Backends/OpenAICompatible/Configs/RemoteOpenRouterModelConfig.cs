using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// OpenRouter（聚合网关，OpenAI 兼容）。候选走运行时模型目录（ADR 0092）。
///
/// 会话亲和不再由这家声明头名：公共头 <c>X-Session-Id</c> 对所有远程模型默认发，它认的正是那一条。
/// 这家真正额外要的是 <b>body 里的 <c>session_id</c></b>——文档给它的定义就是「粘性路由键」，
/// 同时用于用量分组；头是另一条独立的路，两条一起发。
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

    /// <summary>
    /// OpenRouter 的文档字段是 body 里的 <c>session_id</c>（≤256 字符，粘性路由键 + 用量分组）；
    /// <c>prompt_cache_key</c> 是 OpenAI 兼容面的通用写法，两条一起发，值同源。
    /// </summary>
    public override IReadOnlyList<string>? SessionBodyFields => ["session_id", "prompt_cache_key"];
}
