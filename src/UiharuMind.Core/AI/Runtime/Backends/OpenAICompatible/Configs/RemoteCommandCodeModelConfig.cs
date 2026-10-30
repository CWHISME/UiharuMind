using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// Command Code（OpenAI/Anthropic 兼容聚合网关）。候选不写死变体表，
/// 走运行时模型目录（<see cref="ModelCatalogUrl"/>，ADR 0092）。
/// </summary>
[SettingConfigDesc("Command Code")]
[SettingConfigDesc("Command Code", LanguageUtils.ChineseSimplified)]
public class RemoteCommandCodeModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    public override string ModelName { get; set; } = "CommandCode";

    public override string ModelPath { get; set; } = "https://api.commandcode.ai/provider/v1/chat/completions";

    public override string WebsiteUrl { get; set; } = "https://commandcode.ai";

    public override string ModelCatalogUrl { get; set; } = "https://api.commandcode.ai/provider/v1/models";

    public override string ModelDescription { get; set; } = "";

    /// <summary>无默认模型：目录拉下来让用户挑</summary>
    public override string ModelId { get; set; } = "";

    public override int Port { get; set; }

    /// <summary>
    /// 网关下挂的模型各家不同，暂时沿用改动前的写法（<c>thinking.type</c> + <c>reasoning_effort</c>）。
    /// 这套参数在 CommandCode 上是否被接受仍未验证（ADR 0097），换格式前得先录一次真实请求。
    /// </summary>
    protected override EThinkingFormat DefaultThinkingFormat => EThinkingFormat.ThinkingType;

    /// <summary>
    /// 与 OpenRouter 同组：它的模型目录与模型 id 都是 OpenRouter 式命名（<c>meta/…</c>、<c>reflection/…</c>），
    /// 按 pi 的口径这类聚合网关认 <c>x-session-id</c>。
    ///
    /// <b>未验证</b>：加会话头这件事本来就是为它上面的 muse-spark 命中率极低而做的，而此前四头发齐时
    /// 实测毫无改善（muse-spark 的 <c>cached_tokens</c> 恒为 113、冷热交替），所以这只是一条还没被
    /// 证伪的新假设。若这一条与 <c>prompt_cache_key</c> 都上了，命中率仍不动，
    /// 就该认定是它的上游不缓存，客户端这边到此为止。
    /// </summary>
    public override IReadOnlyList<string>? SessionAffinityHeaders => ["x-session-id"];

    /// <summary>
    /// 聚合网关是 <c>prompt_cache_key</c> 最可能生效的一类：它自己不推理，靠这个字段挑上游副本。
    /// 认不认没验证过——开了之后要拿 <c>cache-audit.py</c> 前后比一次，若 <c>cached</c> 仍恒为 113
    /// 就说明它不认，那时把它改回 false，别留着白付一次缓存失效。
    /// </summary>
    public override bool SupportsPromptCacheKey => true;
}
