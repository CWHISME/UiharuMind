namespace UiharuMind.Core.AI.Net.Errors;

/// <summary>
/// 上游在 HTTP 200 的流里回了一条错误帧（<c>{"error":{"code":400,"message":"Error processing stream start"}}</c>，
/// 或 <c>choices[].finish_reason == "error"</c>）。
///
/// <para>
/// 这类帧<b>没有任何正文</b>，SDK 会把它当成「正常结束的一轮空回复」：流照常收尾、没有工具调用、
/// 没有异常。不认出来就等于把一次链路故障记成「模型什么都没说」——上层于是把这一轮当成功收口，
/// 派活方看到空白回执就立刻重发，形成几十秒一轮的失败循环（实机：子代理连续三轮 elapsed=45s 报告 10 字）。
/// </para>
/// <para>
/// 判重发看 <see cref="Transient"/>：4xx（429 除外）是请求体本身的问题，原样重发只会再白付一次上下文；
/// 无码、429 与 5xx 更像节点级抖动，值得隔一拍再试。
/// </para>
/// </summary>
internal sealed class ProviderStreamErrorException : Exception
{
    /// <summary>错误码；服务端没给或用字符串给出且解析不出时为 null</summary>
    public int? Code { get; }

    /// <summary>这一发响应的 id（与日志里的流标识对得上，便于回查）</summary>
    public string? StreamId { get; }

    /// <summary>服务端只给了 <c>finish_reason: "error"</c>、没给 error 对象</summary>
    public bool FinishReasonOnly { get; }

    /// <summary>
    /// 值不值得隔一拍原样重发。
    ///
    /// <para>
    /// 只有 <b>HTTP 4xx 语义</b>（400–499，408/429 除外）算「请求体的问题」：那些原样重发只会再白付一次上下文。
    /// 无码、5xx、408/429，以及<b>小于 400 的域内码</b>都算不明——商汤把「引擎不可用」报成 <c>9</c>，
    /// 同一个码也盖着别的东西，分不清就给两次机会碰运气。
    /// </para>
    /// </summary>
    public bool Transient => Code is not (>= 400 and < 500) || Code is 408 or 429;

    /// <param name="code">错误码</param>
    /// <param name="message">服务端给的错因</param>
    /// <param name="streamId">响应 id</param>
    /// <param name="finishReasonOnly">只有 finish_reason 一种迹象</param>
    public ProviderStreamErrorException(int? code, string? message, string? streamId, bool finishReasonOnly)
        : base(Describe(code, message, streamId, finishReasonOnly))
    {
        Code = code;
        StreamId = streamId;
        FinishReasonOnly = finishReasonOnly;
    }

    private static string Describe(int? code, string? message, string? streamId, bool finishReasonOnly)
    {
        string codeText = code is { } value ? value.ToString() : "无码";
        string reason = string.IsNullOrWhiteSpace(message)
            ? finishReasonOnly ? "服务端以 finish_reason=error 收尾，没有正文" : "服务端没有给出错因"
            : message;
        string id = string.IsNullOrEmpty(streamId) ? string.Empty : $" [stream {streamId}]";
        return $"上游在流里回了错误帧（{codeText}）：{reason}{id}";
    }

    /// <summary>
    /// 沿内部异常链找这一颗。
    ///
    /// <para>
    /// 需要它是因为读出错的包装层数不固定：SDK 可能原样抛出，也可能包进 <c>IOException</c>
    /// （那种情况下 <see cref="TransportFault.IsRetryableDrop"/> 会先命中，按断流跑满五次预算）。
    /// 判定必须认得出「这颗其实是我抛的」，否则同一个故障会按两套完全不同的预算处置。
    /// </para>
    /// </summary>
    /// <param name="error">顶层异常</param>
    /// <returns>链上的第一个实例；没有返回 null</returns>
    public static ProviderStreamErrorException? Find(Exception? error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            if (current is ProviderStreamErrorException found) return found;
        }

        return null;
    }
}
