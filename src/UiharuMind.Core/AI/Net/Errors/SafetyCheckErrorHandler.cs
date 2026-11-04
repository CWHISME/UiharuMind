using UiharuMind.Core.AI.Usage;

namespace UiharuMind.Core.AI.Net.Errors;

/// <summary>
/// 识别上游「安全检查未通过」的 400：
/// <c>{"error":{"message":"safety check failed","type":"failed_precondition_error","code":"9"}}</c>（实测商汤）。
///
/// <para>
/// 与 <see cref="MaxTokensErrorHandler"/>、<see cref="ReasoningRequirementErrorHandler"/> 不同：那两个能从报错里
/// 读出「该改哪个参数」，改完<b>重发的是一发不同的请求</b>；这一句什么都不说，请求体也没有可改的地方，
/// 能做的只有隔一会儿<b>原样重发一次</b>。
/// </para>
/// <para>
/// 值得重发一次的依据是实测而不是客气：商汤把「引擎不可用」与「安全检查未通过」报成同一个码
/// （ADR 0052 已记），而同一份请求体（同会话、同截图、同工具定义、114 条消息）在几分钟后返回 200 且
/// <c>cached_tokens</c> 整段命中；失败的那几次全部落在同一个后端节点（响应回显的 <c>affinity</c> 相同），
/// 同一时间段网关满是 429。也就是说这一码像是节点级抖动，而不是对内容的判决——但这只是推断，
/// 所以下面的处置只买一次「碰运气」，不把它当成可重试的网络故障。
/// </para>
/// <para>
/// 重发的账不大但也不是零：前缀缓存命中，仍按折扣价付一次上下文读取。
/// </para>
/// <para>
/// 只认「400 且正文里明说 safety check failed」。别的 400 各自有自己的毛病，猜错一次就是白烧一整段上下文。
/// </para>
/// </summary>
internal static class SafetyCheckErrorHandler
{
    /// <summary>
    /// 是否为上游安全检查拒绝的错误。
    /// </summary>
    /// <param name="exception">顶层异常；沿 <see cref="Exception.InnerException"/> 逐层找</param>
    /// <returns>命中为 true</returns>
    public static bool IsSafetyCheckFailure(Exception exception)
    {
        // 状态码先挡一道：SDK 之外偶有把同一句话包在别的异常里的情形，没有 400 就不是这一回事
        if (UsageErrorKinds.StatusCodeOf(exception) != 400) return false;

        for (Exception? e = exception; e != null; e = e.InnerException)
        {
            if (e.Message.Contains("safety check failed", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
