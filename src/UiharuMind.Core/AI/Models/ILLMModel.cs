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

using System.Text.Json.Nodes;

namespace UiharuMind.Core.AI.Models;

public interface ILlmModel
{
    string ModelName { get; }
    string ModelPath { get; }
    bool IsVision { get; }
    string ModelDescription { get; }
    string ModelId { get; }
    int Port { get; }

    /// <summary>
    /// 追加到请求体的额外参数。按模型配置的思考力度产出,一档可对应多个参数
    /// (如同时给出 thinking 与 reasoning_effort,各后端自行取舍)。
    /// </summary>
    /// <returns>要写入请求 JSON 的键值对;无需追加时为空</returns>
    public IReadOnlyList<KeyValuePair<string, JsonNode?>>? GetExtraParams() => null;

    /// <summary>
    /// 本次请求的思考档位 wire 值。请求侧改写据此判断本次是否可能处于思考模式：
    /// <see cref="ThinkingEfforts.Off"/> 显式发 <c>thinking: disabled</c>，服务端按正文调用处理，
    /// 不要求带 tool_calls 的助手消息补思考字段；其余档位都可能思考，需保守补上。
    /// 本地模型没有思考档位，取 <see cref="ThinkingEfforts.Auto"/>。
    /// </summary>
    public string ThinkingEffort => ThinkingEfforts.Auto;

    /// <summary>
    /// 历史消息里<b>已有</b>思考正文时，是否原样回传（有正文看这个，没正文看
    /// <see cref="RequireReasoningOnEveryAssistantMessage"/>）。
    /// <c>null</c>=自动(历史有思考就回传);<c>true</c>/<c>false</c>=显式要求/禁止。
    /// 本地模型没有思考回传一说,默认不回传。
    /// </summary>
    public bool? ReplayExistingReasoning => false;

    /// <summary>
    /// 回填思考正文用的<b>请求侧</b> wire 键名(reasoning_content/reasoning/reasoning_text)。
    /// 取预设表声明，否则默认 <c>reasoning_content</c>；<b>不取响应侧观测值</b>——
    /// 两者不是一回事（CommandCode 的响应给 reasoning、请求只认 reasoning_content）。本地模型取默认。
    /// </summary>
    public string ThinkingField => "reasoning_content";

    /// <summary>
    /// <b>每一条</b>助手消息都必须带思考字段(有正文回正文,没正文补空串)。
    /// 服务端要的是「字段必须在」,与本次是否思考无关,不跟思考档位走。
    /// 本地模型没有这条约束,默认不带。
    /// </summary>
    public bool RequireReasoningOnEveryAssistantMessage => false;

    /// <summary>
    /// 是否不发送采样参数(temperature/top_p/presence_penalty/frequency_penalty)。
    /// 采样参数固定的模型(如 Kimi)会拒绝显式传值的请求,默认发送,按模型单独开启。
    /// </summary>
    public bool OmitSamplingParams => false;

    /// <summary>
    /// 会话亲和要写哪几个<b>请求头名</b>（值由会话统一给出，见
    /// <see cref="Chat.ChatSession.CacheAffinity"/>）；空或 null = 不发。
    ///
    /// 网关按副本各存一份前缀缓存，同一会话的请求落到同一台副本才谈得上命中，而各家认的头名不同：
    /// 认错的头只会被忽略，代价却在另一头——一次发齐会撞上别家的语义
    /// （<c>x-client-request-id</c> 是「请求」标识、<c>session_id</c> 是下划线头）。
    /// 所以由供应商自己声明（如 OpenRouter 只认 <c>x-session-id</c>），公共层不猜端点。
    /// 本地模型与不认的网关不发，默认空。
    /// </summary>
    public IReadOnlyList<string>? SessionAffinityHeaders => null;

    /// <summary>
    /// 是否发 body 级的 <c>prompt_cache_key</c>（值取会话亲和值，同一会话恒定）。
    ///
    /// 与亲和头是<b>两条独立的路</b>：头由网关看、body 字段由服务端看，
    /// 服务端用它把同一会话的前缀钉在同一份缓存上，命中率比只靠头更可控。
    /// 但它<b>会改字节</b>——第一次发会让已有缓存失效一次，且严格服务端会因未知字段 400，
    /// 所以默认关，只由认它的供应商打开（opencode 的 <c>supportsPromptCacheKey</c> 同口径）。
    /// </summary>
    public bool SupportsPromptCacheKey => false;

    /// <summary>
    /// 这个模型的<b>供应商标识</b>（谁签的票）。
    ///
    /// 加密思考票据是「只有原厂认得的凭证」，换供应商就没有意义——而网关自己不校验
    /// （实测把 Meta 的票发给 deepseek 照样 200），所以门禁只能在这一侧。
    /// 只比 <see cref="ModelId"/> 不够：同一个模型 id 在不同供应商下是不同的东西。
    ///
    /// 取配置类型名（<c>RemoteCommandCodeModelConfig</c> 这类），本地模型取空。
    /// </summary>
    public string ProviderId => GetType().Name;

    /// <summary>
    /// 这个模型答话的 <b>wire 格式</b>：走 <c>/chat/completions</c> 还是 <c>/responses</c>。
    ///
    /// 两种格式的请求体形状不同（<c>messages</c> vs <c>input</c> item 数组）、
    /// 响应形状不同（<c>delta</c> vs 逐类 SSE 事件）、加密思考票据的承载也不同
    /// （消息上的 <c>reasoning_details</c> vs item 上的 <c>encrypted_content</c>），
    /// 所以它决定整条链路上哪一套改写器与解析器生效。
    ///
    /// 声明在模型上而不是猜端点：同一个网关下不同模型支持的格式可能不同。
    /// </summary>
    public EApiFormat ApiFormat => EApiFormat.ChatCompletions;
}