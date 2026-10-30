using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Reasoning;

namespace UiharuMind.Core.AI.Net.Request;

/// <summary>
/// 一次请求体改写「要做什么」：由模型配置与本次请求上下文推出来的一张清单，
/// 与「怎么做」（<see cref="ChatCompletionsRequestRewriter"/> 的字节级拼接）分开。
///
/// 分开的理由是可测性：清单能拿模型声明直接断言，不必去读改写后的字节；
/// 反过来改写器只关心字节，不再知道 <c>SupportsPromptCacheKey</c> 这类供应商知识。
///
/// <para><b>回填思考不在这张清单里</b>（ADR 0101）：思考跟着消息对象走，由
/// <see cref="ReasoningReplayChatClient"/> 在把消息交给 SDK 之前绑好。
/// 这里只剩「字段必须在」那半条——补空串不需要知道是哪条消息。</para>
/// </summary>
/// <param name="ExtraParams">模型的额外参数，写到根上（同名覆盖）</param>
/// <param name="ForbidToolCalls">写 <c>tool_choice: "none"</c></param>
/// <param name="OmitSamplingParams">删掉 <see cref="SamplingParamKeys"/></param>
/// <param name="FillEmptyReasoning">
/// 每一条助手消息都带思考字段：没有正文的补空串。与 opencode 的 <c>compatibility.requireReasoning</c>、
/// pi 的「有就带」同一层级——服务端要的是「字段必须在」，不是「字段必须有内容」。
/// </param>
/// <param name="ReasoningField">思考正文的 wire 键名（预设/默认）</param>
/// <param name="PromptCacheKey">
/// body 级的 <c>prompt_cache_key</c>：会话亲和值，服务端据此把同一会话的前缀钉在同一份缓存上。
/// 由供应商开关（<see cref="Models.ILlmModel.SupportsPromptCacheKey"/>），没开或没会话时为 null
/// </param>
internal readonly record struct RequestRewrite(
    IReadOnlyList<KeyValuePair<string, JsonNode?>>? ExtraParams,
    bool ForbidToolCalls,
    bool OmitSamplingParams,
    bool FillEmptyReasoning = false,
    string ReasoningField = ReasoningReplayPolicy.DefaultField,
    string? PromptCacheKey = null)
{
    /// <summary>
    /// 开启 <see cref="OmitSamplingParams"/> 的模型要删掉的采样参数。只删这四个,
    /// <c>max_tokens/thinking/tool_choice</c> 等不受影响。
    /// </summary>
    internal static readonly string[] SamplingParamKeys =
        ["temperature", "top_p", "presence_penalty", "frequency_penalty"];

    /// <summary>
    /// 按模型配置与<b>当前</b>请求上下文（<see cref="LlmRequestScope.Current"/>）收集要做的改写。
    /// 生产路径只走这一个入口：改写发生在 SDK 管道里，拿不到调用方手里的上下文。
    /// </summary>
    /// <param name="model">目标模型；为 null 时只做与模型无关的改写</param>
    /// <returns>要做的改写</returns>
    public static RequestRewrite For(ILlmModel? model) => For(model, LlmRequestScope.Current);

    /// <summary>
    /// 按模型配置与给定上下文收集要做的改写——纯函数，不读环境。
    ///
    /// 分开的理由与清单本身一样是可测性：断言「什么情况下要回填」不该先摆好一个异步流，
    /// 摆的时候还得小心别漏到别的测试里。
    /// </summary>
    /// <param name="model">目标模型；为 null 时只做与模型无关的改写</param>
    /// <param name="context">本次请求的上下文</param>
    /// <returns>要做的改写</returns>
    public static RequestRewrite For(ILlmModel? model, LlmRequestContext context)
    {
        // 每条助手消息都要带思考的模型：有正文回正文，没正文补空串。
        // 这条校验发生在服务端解析历史那一层，与本次是否思考无关，因此不跟思考档位走
        bool everyAssistant = model is not null && model.RequireReasoningOnEveryAssistantMessage;

        return new RequestRewrite(
            model?.GetExtraParams(),
            context.ForbidToolCalls,
            // 采样参数固定的模型(如 Kimi)会拒绝显式传值的请求,开启后删掉这四个字段,不碰其它参数
            model?.OmitSamplingParams == true,
            everyAssistant,
            // 思考回填键名不写死:按模型实际字段(预设/默认)
            ReasoningReplayPolicy.FieldName(model),
            // 会话亲和的 body 形态:值就是亲和值(32 字符,在 OpenAI 的 64 上限内),
            // 没开这个开关或没有会话时为 null,改写整段跳过
            model?.SupportsPromptCacheKey == true ? context.SessionAffinity : null);
    }
}
