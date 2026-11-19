namespace UiharuMind.Core.AI.Net.Errors;

/// <summary>
/// 请求发出去了，但响应头迟迟不来：建连 / 上传 / 等服务端回头的整段路上没有任何字节回来。
///
/// <para>
/// 为什么会有这个异常：重试决策之后、响应头之前这段路（<see cref="Transport.WireHttpHandler"/> 的
/// <c>base.SendAsync</c>）没有超时——<c>HttpClient.Timeout</c>、SDK <c>NetworkTimeout</c> 都被有意关成
/// Infinite（长思考不能被静态时限误杀），而唯一的看门狗在响应体读取上（<see cref="ProviderStreamIdleException"/>）
/// 只管 200 + SSE 之后的阶段。等响应头一旦挂死（连接被黑洞 / 上传被网关丢弃），上层每一层
/// （重试预算、看门狗、用量账本）全部失明，唯一脱身路径是用户按停止。
/// </para>
/// <para>
/// 继承 <see cref="IOException"/> 且不携带状态码：判据交给 <see cref="TransportFault.IsRetryableDrop"/>，
/// 处置与「流被掐断」一样走整段重发（读静默的 <see cref="ProviderStreamIdleException"/> 同款协议）。
/// </para>
/// </summary>
internal sealed class ResponseHeaderTimeoutException : IOException
{
    /// <summary>
    /// 等响应头的上限
    /// </summary>
    public TimeSpan HeaderTimeout { get; }

    /// <param name="headerTimeout">等响应头的上限</param>
    /// <param name="innerException">取消时抛出的原始异常（保留堆栈用）</param>
    public ResponseHeaderTimeoutException(TimeSpan headerTimeout, Exception? innerException = null)
        : base($"timed out waiting for response headers after {headerTimeout.TotalSeconds:0}s", innerException)
    {
        HeaderTimeout = headerTimeout;
    }
}