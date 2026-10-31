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
    /// 是否发会话亲和。<b>一层总开关</b>：关掉时公共头、协议头、供应商追加头与 body 字段一起不发。
    ///
    /// 本地模型默认关（进程内或单副本，没有「落到哪台」这回事）；远程模型配置默认开——
    /// 公共头广谱且不参与前缀缓存，多带一条不认识的只是噪音，漏带一条却是整段冷启动。
    /// </summary>
    public bool SendsSessionAffinity => false;

    /// <summary>
    /// 公共头之外<b>还要追加</b>哪几个请求头名（值与会话统一给出，见
    /// <see cref="Chat.ChatSession.CacheAffinity"/>）；空或 null = 只发公共头与协议头。
    ///
    /// 公共头（<see cref="SessionAffinity.CommonHeaders"/>）由 HTTP 层默认发，这里只放这家独有的：
    /// opencode 系认 <c>x-opencode-session</c>（缺了直接 400），LiteLLM 认 <c>X-Litellm-Session-Id</c>。
    /// 同名（大小写不敏感）会被去重，供应商再写一遍 <c>x-session-id</c> 也只是同一个头。
    /// </summary>
    public IReadOnlyList<string>? SessionAffinityHeaders => null;

    /// <summary>
    /// body 级要写哪几个会话字段（值取会话亲和值，同一会话恒定），如
    /// <c>session_id</c>（OpenRouter 约定）、<c>prompt_cache_key</c>（OpenAI 兼容面）。
    ///
    /// 与亲和头是<b>两条独立的路</b>：头由网关看、body 字段由服务端看，
    /// 服务端用它把同一会话的前缀钉在同一份缓存上，命中率比只靠头更可控。
    /// 但它<b>会改字节</b>——第一次发会让已有缓存失效一次，且严格服务端会因未知字段 400，
    /// 所以默认空，只由认它的供应商打开（opencode 的 <c>supportsPromptCacheKey</c> 同口径）。
    /// </summary>
    public IReadOnlyList<string>? SessionBodyFields => null;

    /// <summary>
    /// 显式缓存断点的保留时长。只对<b>显式断点型</b>的服务端有意义（Anthropic Messages）：
    /// 自动前缀型由服务端自己决定存多久，客户端说了不算。
    ///
    /// 默认 <see cref="ECacheRetention.Long"/>（1 小时）：会话里常有几分钟以上的停顿
    /// （看代码、跑测试、等用户回话），5 分钟的默认寿命一过，下一发就是整段冷启动。
    /// 代价是<b>写入更贵</b>——1 小时的缓存写入按 2 倍输入计价，5 分钟那档是 1.25 倍。
    /// 连续不停敲的会话用 <see cref="ECacheRetention.Short"/> 更省。
    /// </summary>
    public ECacheRetention CacheRetention => ECacheRetention.Long;

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
    /// 模型最大输出预算（token 数）。0 表示未设置，由各协议自己取默认。
    ///
    /// 思考与正文共用这一个配额：给小了会被思考吃光，正文一个字都发不出。
    /// 各协议的发法不同——chat 线把它写进请求体的 <c>max_tokens</c>，
    /// Anthropic 线由 SDK 写进 <c>MessageCreateParams.MaxTokens</c>。
    /// </summary>
    public int MaxTokens => 0;

    /// <summary>
    /// 这个模型答话的 <b>wire 格式</b>：走 <c>/chat/completions</c>、<c>/responses</c> 还是
    /// Anthropic 的 <c>/v1/messages</c>。
    ///
    /// 三种格式的请求体形状不同（<c>messages</c> vs <c>input</c> item 数组 vs 顶层 <c>system</c> +
    /// block 数组的 <c>content</c>）、响应形状不同（<c>delta</c> vs 逐类 SSE 事件）、
    /// 加密思考票据的承载也不同（消息上的 <c>reasoning_details</c> vs item 上的
    /// <c>encrypted_content</c> vs thinking 块里的 <c>signature</c>），
    /// 所以它决定整条链路上哪一套改写器与解析器生效。
    ///
    /// 声明在模型上而不是猜端点：同一个网关下不同模型支持的格式可能不同。
    /// </summary>
    public EApiFormat ApiFormat => EApiFormat.ChatCompletions;
}