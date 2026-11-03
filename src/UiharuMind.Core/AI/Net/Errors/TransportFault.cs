using System.Net.Sockets;
using System.Security.Authentication;

namespace UiharuMind.Core.AI.Net.Errors;

/// <summary>
/// 传输层故障的判据：这一次失败值不值得整段重发。
///
/// <para>
/// 判据是<b>基类</b> <see cref="IOException"/>，不是 <see cref="HttpIOException"/>：同一次「服务端把流掐了」，
/// .NET 有时包成 <c>HttpIOException</c>（<c>ResponseEnded</c>），有时把 SslStream 的裸
/// <c>IOException</c>（<c>Received an unexpected EOF or 0 bytes from the transport stream</c>）
/// 直接放出来。实测两者约 8:1——只认 <c>HttpIOException</c> 就会漏掉裸的那种，而漏掉的那一种会整轮作废。
/// </para>
/// <para>
/// <see cref="HttpRequestException"/> 只在<b>没有状态码</b>时算：有状态码说明服务端回过响应，
/// 4xx/5xx 由调用方按业务处理，重发只会重复同样的结果。没有状态码的是连接层失败
/// （传一半被 reset），重发安全且通常真能过去。
/// </para>
/// <para>
/// 两类例外不重发：拨不通（连接被拒、主机/网络不可达，对端根本没起来）与 TLS 握手失败
/// （证书/协议不匹配，重发一百次还是失败）。这两类交给上层立刻报错。
/// </para>
/// </summary>
internal static class TransportFault
{
    /// <summary>
    /// 这个异常算不算「连接断了」——可以整段重发的那一种
    /// </summary>
    /// <param name="error">流式读抛出的异常</param>
    /// <returns>值得重发返回 true</returns>
    public static bool IsRetryableDrop(Exception error)
    {
        if (error is HttpRequestException { StatusCode: not null }) return false;
        if (error is not (IOException or HttpRequestException)) return false;
        return !HasUnrecoverableCause(error);
    }

    /// <summary>异常链里有没有「重发也不会变」的成因：拨不通、TLS 握手失败</summary>
    private static bool HasUnrecoverableCause(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            if (current is AuthenticationException) return true;
            if (current is not SocketException socket) continue;
            if (socket.SocketErrorCode is SocketError.ConnectionRefused or SocketError.HostUnreachable
                or SocketError.NetworkUnreachable or SocketError.HostNotFound) return true;
        }

        return false;
    }
}
