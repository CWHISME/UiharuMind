namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// 单个 ModelId 的预设:默认上下文大小、默认输出预算与是否支持视觉
/// </summary>
/// <param name="ContextLength">默认上下文窗口(token 数),0 表示未设置</param>
/// <param name="MaxTokens">默认最大输出预算(token 数),0 表示未设置</param>
/// <param name="IsVision">是否支持视觉</param>
/// <param name="RequiresReasoningContentRoundtrip">
/// 思考模式下,助手消息带 tool_calls 时是否要求原样带回当时的 reasoning_content。
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
public record RemoteModelIdVariant(
    int ContextLength = 0,
    int MaxTokens = 0,
    bool IsVision = false,
    bool RequiresReasoningContentRoundtrip = false,
    bool OmitSamplingParams = false,
    string Alias = "")
{
    /// <summary>
    /// 空表,供未声明预设的配置使用
    /// </summary>
    public static IReadOnlyDictionary<string, RemoteModelIdVariant> Empty { get; } =
        new Dictionary<string, RemoteModelIdVariant>();
}