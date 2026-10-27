namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// 单个 ModelId 的预设:默认上下文大小、默认输出预算与是否支持视觉
/// </summary>
/// <param name="ContextLength">默认上下文窗口(token 数),0 表示未设置</param>
/// <param name="MaxTokens">默认最大输出预算(token 数),0 表示未设置</param>
/// <param name="IsVision">是否支持视觉</param>
/// <param name="ReplayExistingReasoning">
/// 历史消息里<b>已有</b>思考正文时,是否原样回传（有正文看这个，没正文看
/// <paramref name="RequireReasoningFieldWhenEmpty"/>）。
/// <c>null</c>=自动(历史有思考就回传);<c>true</c>/<c>false</c>=显式要求/禁止。
/// 这是模型本身(如 DeepSeek)的接口约束,与转售它的供应商无关——同一供应商下的其它
/// 模型(如商汤转售的 GLM/自研模型)未必有此限制,因此按 ModelId 逐条声明,不挂在配置类上。
/// </param>
/// <param name="OmitSamplingParams">
/// 是否不发送采样参数(temperature/top_p/presence_penalty/frequency_penalty)。
/// 采样参数固定的模型(如 Kimi)会拒绝显式传值的请求,因此按 ModelId 逐条声明。
/// </param>
/// <param name="Alias">
/// 显示别名,非空时创建窗口的下拉框优先展示它(如标注「免费」);空则回退显示 ModelId。
/// </param>
/// <param name="ThinkingField">
/// 该模型的思考 wire 字段名(reasoning_content/reasoning/reasoning_text);空表示未声明,
/// 消费时按 运行时观测值 → 本预设值 → reasoning_content 查表。
/// </param>
/// <param name="RequireReasoningFieldWhenEmpty">
/// 思考模式下助手消息带 tool_calls 时,<b>没有思考正文也必须带思考字段</b>(空串)。
/// 没正文看这个，有正文看 <paramref name="ReplayExistingReasoning"/>——两者互斥：
/// 有正文时字段已存在，前者不适用；没正文时没东西可回传，后者不适用。
/// DeepSeek 类服务端把「字段必须在」当硬校验,缺字段直接 400
/// (历史里的旧工具调用没有思考正文——先用关思考的档位跑出来的——是常见触发点)。
/// 消费侧仅在本次请求非 None 档时生效（正文调用不要求该字段）。
/// </param>
public record RemoteModelIdVariant(
    int ContextLength = 0,
    int MaxTokens = 0,
    bool IsVision = false,
    bool? ReplayExistingReasoning = null,
    bool OmitSamplingParams = false,
    string Alias = "",
    string ThinkingField = "",
    bool RequireReasoningFieldWhenEmpty = false)
{
    /// <summary>
    /// 空表,供未声明预设的配置使用
    /// </summary>
    public static IReadOnlyDictionary<string, RemoteModelIdVariant> Empty { get; } =
        new Dictionary<string, RemoteModelIdVariant>();
}