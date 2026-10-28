namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// 单个 ModelId 的预设:默认上下文大小、默认输出预算与是否支持视觉
/// </summary>
/// <param name="ContextLength">默认上下文窗口(token 数),0 表示未设置</param>
/// <param name="MaxTokens">默认最大输出预算(token 数),0 表示未设置</param>
/// <param name="IsVision">是否支持视觉</param>
/// <param name="ReplayExistingReasoning">
/// 历史消息里<b>已有</b>思考正文时,是否原样回传（有正文看这个，没正文看
/// <paramref name="RequireReasoningOnEveryAssistantMessage"/>）。
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
/// <param name="RequireReasoningOnEveryAssistantMessage">
/// <b>每一条</b>助手消息都必须带思考字段:有正文回正文,没正文补空串。
/// 缺了报 <c>Missing required parameter: 'messages[n].reasoning_content'</c>
/// （实测 CommandCode 的 reflection/beam:free 就是这条）。
/// 这条校验与本次是否思考无关,消费侧不跟思考档位走。
/// 与 opencode 的 <c>compatibility.requireReasoning</c> 同一层级。
/// </param>
public record RemoteModelIdVariant(
    int ContextLength = 0,
    int MaxTokens = 0,
    bool IsVision = false,
    bool? ReplayExistingReasoning = null,
    bool OmitSamplingParams = false,
    string Alias = "",
    string ThinkingField = "",
    bool RequireReasoningOnEveryAssistantMessage = false)
{
    /// <summary>
    /// 空表,供未声明预设的配置使用
    /// </summary>
    public static IReadOnlyDictionary<string, RemoteModelIdVariant> Empty { get; } =
        new Dictionary<string, RemoteModelIdVariant>();
}