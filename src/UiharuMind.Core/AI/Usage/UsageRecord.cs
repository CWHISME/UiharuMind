using System.ClientModel;

namespace UiharuMind.Core.AI.Usage;

/// <summary>
/// 一次请求的用量明细（写入侧）。<b>模型身份是快照</b>：配置会改名、会被删，
/// 历史消费记录必须回答「当时用的是哪个模型」，只能靠把三个维度一起冗余进行里
/// </summary>
/// <param name="Ts">请求完成时刻，Unix 毫秒</param>
/// <param name="StartedTs">请求发出时刻，算延迟用</param>
/// <param name="SessionId">会话标识（会话删了记录保留孤儿，审计不可变）</param>
/// <param name="ModelName">用户可见名，也是配置字典键</param>
/// <param name="ModelId">发给服务端的 id；本地为空串</param>
/// <param name="ProviderId">配置类型名；本地为 <c>local</c></param>
/// <param name="ProviderKey">定价表键，查不到为空串</param>
/// <param name="ApiBase">归一化 base url，区分同家多实例</param>
/// <param name="IsLocal">是否本地模型（本地不计价）</param>
/// <param name="InputTokens">输入（含命中缓存的输入）</param>
/// <param name="CachedRead">其中命中前缀缓存的部分</param>
/// <param name="CachedWrite">写入缓存；null 表示供应商没上报（≠ 0）</param>
/// <param name="OutputTokens">输出</param>
/// <param name="ReasoningTokens">其中思考 token</param>
/// <param name="LatencyMs">总延迟；null 表示未测得</param>
/// <param name="FirstTokenMs">首 token 延迟；null 表示未测得</param>
/// <param name="Ok">这次服务调用是否成功收口</param>
/// <param name="ErrorKind">失败类别（<see cref="UsageErrorKinds"/> 的常量）；成功为 null</param>
/// <param name="ErrorDetail">失败行存简短错误消息（已截断）；成功为 null</param>
/// <param name="StatusCode">HTTP 状态码；没有为 null</param>
/// <param name="RetryCount">这次服务调用内部的重发次数（断线退避/自愈重发都算）；旁路请求只能拿到总数，记在最后一行</param>
/// <param name="CostUsd">估算费用；null = 未定价（≠ 免费）</param>
/// <param name="PriceSource">价格来源 <c>official</c> / <c>modelsdev</c>；未定价为 null</param>
/// <param name="PriceInput">计价时用的输入价（USD / 百万 token），留证回溯</param>
/// <param name="PriceOutput">计价时用的输出价</param>
/// <param name="PriceCacheRead">计价时用的缓存读价；供应商未单独公布时为 null</param>
/// <param name="Multiplier">峰时/促销倍率（仅 CommandCode 一家当前有数据）</param>
public sealed record UsageRecord(
    long Ts,
    long StartedTs,
    string SessionId,
    string ModelName,
    string ModelId,
    string ProviderId,
    string ProviderKey,
    string ApiBase,
    bool IsLocal,
    long InputTokens,
    long CachedRead,
    long? CachedWrite,
    long OutputTokens,
    long ReasoningTokens,
    int? LatencyMs,
    int? FirstTokenMs,
    bool Ok,
    string? ErrorKind,
    string? ErrorDetail,
    int? StatusCode,
    int RetryCount,
    decimal? CostUsd,
    string? PriceSource,
    decimal? PriceInput,
    decimal? PriceOutput,
    decimal? PriceCacheRead,
    decimal Multiplier);

/// <summary>
/// 失败类别的取值。<b>用字符串不用枚举</b>：以后加错误类别不用改表。
/// 唯一的铁律是 <see cref="Cancel"/> 不计入错误率——用户自己按的停止不是服务失败
/// </summary>
public static class UsageErrorKinds
{
    /// <summary>网络 / HTTP 层失败</summary>
    public const string Http = "http";

    /// <summary>超时（客户端读超时、交接侧的非用户取消 OCE）</summary>
    public const string Timeout = "timeout";

    /// <summary>用户主动取消（不计错误率）</summary>
    public const string Cancel = "cancel";

    /// <summary>流中途断开 / 其它服务端错误</summary>
    public const string Stream = "stream";

    /// <summary>用户取消是否不算错误率</summary>
    /// <param name="errorKind">错误类别</param>
    /// <returns>是用户取消返回 true</returns>
    public static bool IsUserCancelled(string? errorKind) =>
        string.Equals(errorKind, Cancel, StringComparison.Ordinal);

    /// <summary>
    /// 把异常归类成错误类别。取消与超时的区别只在一个 <c>when</c> 条件，
    /// 调用方必须自己先判「用户是否按了停止」，这里只处理剩下那两种情况
    /// </summary>
    /// <param name="error">异常</param>
    /// <returns>错误类别常量</returns>
    public static string Classify(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            switch (current)
            {
                case TimeoutException:
                case TaskCanceledException:
                case OperationCanceledException:
                    return Timeout;
                case HttpRequestException:
                case IOException:
                //SDK 把非 2xx 包成 ClientResultException（带 Status），它就是一次 HTTP 层失败
                case ClientResultException:
                    return Http;
            }
        }

        return Stream;
    }

    /// <summary>从异常链里取 HTTP 状态码</summary>
    /// <param name="error">异常</param>
    /// <returns>状态码；取不到为 null</returns>
    public static int? StatusCodeOf(Exception? error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: { } status }) return (int)status;
            //SDK 的 4xx/5xx：状态码只在这个例外上
            if (current is ClientResultException clientResult && clientResult.Status > 0) return clientResult.Status;
        }

        return null;
    }

    /// <summary>错误详情入库前的截断：一行简短说明够了，不把整个堆栈塞进库</summary>
    /// <param name="error">异常</param>
    /// <returns>至多 300 字的描述</returns>
    public static string? DetailOf(Exception? error)
    {
        if (error == null) return null;
        string text = $"{error.GetType().Name}: {error.Message}".Replace('\r', ' ').Replace('\n', ' ');
        return text.Length > 300 ? text[..300] : text;
    }
}
