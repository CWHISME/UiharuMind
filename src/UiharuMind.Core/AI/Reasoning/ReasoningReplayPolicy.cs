using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Reasoning;

/// <summary>
/// 历史里的思考要不要跟着消息发回去，以及发到哪个键名上——由<b>模型声明</b>决定，
/// 与「怎么把它写进请求体」无关（后者是各协议自己的事，见 ADR 0101）。
///
/// <para>放在这里的理由：chat 线的绑定器与改写器都要问这两个问题。抄成两份的话，
/// 一处改了另一处不知道，就会出现「绑定器写了思考、改写器又按旧口径补空串」这种自相矛盾的请求体。</para>
/// </summary>
internal static class ReasoningReplayPolicy
{
    /// <summary>模型没给回填键名时的默认 wire 键名</summary>
    internal const string DefaultField = "reasoning_content";

    /// <summary>
    /// 这一轮要不要回填历史思考。
    ///
    /// <para><c>null</c>/<c>true</c> 都回填：<c>null</c> = 自动（历史有思考就带上），
    /// <c>true</c> = 模型强制要求。只有显式 <c>false</c>（确认不要求回传）或无模型才跳过。</para>
    ///
    /// <para>注意不能用 <c>model?.ReplayExistingReasoning != false</c>：nullable 的 lifted 比较
    /// 在 null 时得到 null（判不出），必须先判模型存在。</para>
    /// </summary>
    /// <param name="model">目标模型</param>
    /// <returns>要回填时为 true</returns>
    public static bool ShouldReplay(ILlmModel? model) =>
        model is not null
        && (model.RequireReasoningOnEveryAssistantMessage || model.ReplayExistingReasoning is not false);

    /// <summary>
    /// 回填用的 wire 键名：取模型给的请求侧键名，空串退回 <see cref="DefaultField"/>
    /// ——空串拼进请求体就是 <c>"": "..."</c>，服务端认不出。
    ///
    /// <para>观测到的响应侧字段名不参与：那两家未必同名（实测 CommandCode 响应 <c>reasoning</c>、
    /// 请求要 <c>reasoning_content</c>）。</para>
    /// </summary>
    /// <param name="model">目标模型</param>
    /// <returns>生效的键名</returns>
    public static string FieldName(ILlmModel? model) =>
        string.IsNullOrWhiteSpace(model?.ThinkingField) ? DefaultField : model!.ThinkingField;
}
