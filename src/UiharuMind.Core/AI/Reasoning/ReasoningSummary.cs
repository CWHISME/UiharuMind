namespace UiharuMind.Core.AI.Reasoning;

/// <summary>
/// 思考摘要的出口：把明文摘要交给界面。
///
/// 摘要与密文是<b>两条独立的路</b>：
/// - 密文（<c>reasoning.encrypted</c>）只有服务端认，得原样带回才能续接推理链
/// - 摘要（<c>reasoning.summary</c>）是明文，给人看的，但<b>服务端不认回传</b>
///
/// 所以摘要在请求体里没有位置——带回去只是白改字节、白打缓存。
///
/// 有些闭源模型只给密文（实测 <c>reasoning_details</c> 条目类型只有
/// <c>reasoning.encrypted</c>），摘要是空的，界面只能开一张没有正文的「加密思考」卡；
/// 另一些闭源模型会下发摘要，那才有真东西可显示。
/// </summary>
internal static class ReasoningSummary
{
    /// <summary>
    /// 从切出来的 <c>reasoning_details</c> 数组里取明文摘要。
    /// </summary>
    /// <param name="details">数组原文</param>
    /// <returns>摘要正文；没有摘要条目时为 null</returns>
    public static string? Read(byte[]? details) =>
        details == null ? null : ReasoningDetailExtractor.ReadSummary(details);
}
