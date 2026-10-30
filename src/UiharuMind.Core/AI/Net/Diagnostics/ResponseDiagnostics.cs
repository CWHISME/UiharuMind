using System.Net.Http.Headers;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Net.Diagnostics;

/// <summary>
/// 一次响应留在日志里的那几行：成功时一行状态、失败时状态加正文。
///
/// 单独成一处是因为它们<b>只是观测</b>——不参与请求、不影响响应内容，
/// 出问题时要能整块关掉而不牵动任何转发逻辑。
/// </summary>
internal static class ResponseDiagnostics
{
    //各家名字都不一样(OpenAI 用 x-ratelimit-*,Anthropic 用 anthropic-ratelimit-*,
    //部分国内网关用 x-tc-requestid 之类),因此按子串命中而不是白名单精确匹配
    private static readonly string[] DiagnosticHeaderHints =
        ["ratelimit", "rate-limit", "retry-after", "request-id", "requestid"];

    /// <summary>
    /// 记一行响应状态。
    ///
    /// 媒体类型无条件记录：<see cref="Response.ResponseSanitizer"/> 只在 <c>text/event-stream</c> 时介入，
    /// 非该媒体类型的流不经过 SSE 清洗，这类故障的桩也就全都不在链路上。
    /// 顺带记下这一发的亲和与网关回显：命中率不对时，第一个要回答的问题是「到底发了什么、对方回了什么」。
    /// </summary>
    /// <param name="response">服务端响应</param>
    /// <param name="affinity">实际写出去（或没写出去）的亲和值</param>
    /// <param name="echo">网关回显的亲和头（<see cref="Transport.SessionAffinityHeaders.Echo"/>），没有为 null</param>
    /// <param name="isChatCompletions">这条路径的响应会不会被清洗</param>
    public static void LogResponse(HttpResponseMessage response, string affinity, string? echo,
        bool isChatCompletions)
    {
        Log.Debug($"OpenAI-compatible response: {(int)response.StatusCode} {response.ReasonPhrase}, " +
                  $"content-type: {response.Content?.Headers.ContentType?.MediaType ?? "(null)"}" +
                  (isChatCompletions ? "" : " (not sanitized)") +
                  $", affinity: {affinity}" +
                  (echo == null ? "" : $", gateway echo: {echo}"),
            ELogCategory.LlmResponse);
    }

    /// <summary>
    /// 失败诊断日志。**只在失败时说话**——成功路径上曾经打过一行配额头，
    /// 那是为了查清撞的到底是 TPM 还是 RPM；结论已经有了（免费档端点一个配额头都不给，
    /// 只回 <c>X-Request-ID</c>，限流来自平台侧共享容量），那行日志就只剩噪音，已删。
    ///
    /// 失败时记全：429 的正文里通常写明撞的是哪个限额与当前上限，
    /// 请求标识则是找厂商开工单时要给的。
    /// </summary>
    /// <param name="response">服务端响应</param>
    /// <param name="uri">请求地址：本地模型看端口对不对得上在跑的那个服务</param>
    /// <param name="cancellationToken">取消标记</param>
    public static async Task LogFailureAsync(HttpResponseMessage response, Uri? uri,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        string? diagnostics = FormatDiagnosticHeaders(response);
        string head = $"OpenAI-compatible request failed: {(int)response.StatusCode} {response.ReasonPhrase} " +
                      $"({uri?.GetLeftPart(UriPartial.Path)})";
        if (diagnostics != null) head += " | " + diagnostics;

        byte[] body = await ReadBodyAsync(response, cancellationToken);
        if (body.Length == 0)
        {
            Log.Warning(head, ELogCategory.LlmResponse);
            return;
        }

        //不压成单行:日志面板自己会做单行摘要+详情展开,压了反而看不了格式
        using PooledByteWriter formatted = new(body.Length + body.Length / 4);
        try
        {
            LlmBodyLogFormat.Format(body, formatted);
        }
        catch (Exception e)
        {
            // 失败日志本身出错不能盖掉这次响应:正文不要了,状态码与诊断头照记
            Log.Warning($"{head}\n(response body could not be formatted: {e.GetType().Name}: {e.Message})",
                ELogCategory.LlmResponse);
            return;
        }

        Log.Warning(head + "\n", formatted.WrittenSpan, ELogCategory.LlmResponse);
    }

    /// <summary>
    /// 收集响应里的诊断头，拼成一行
    /// </summary>
    /// <param name="response">服务端响应</param>
    /// <returns>形如 <c>a=1; b=2</c> 的文本；一个都没命中时为 null</returns>
    internal static string? FormatDiagnosticHeaders(HttpResponseMessage response)
    {
        List<string> parts = [];
        Collect(response.Headers);
        Collect(response.Content?.Headers);
        if (parts.Count == 0) return null;

        parts.Sort(StringComparer.OrdinalIgnoreCase); //头的枚举顺序无保证,排序后日志与测试都稳定
        return string.Join("; ", parts);

        void Collect(HttpHeaders? headers)
        {
            if (headers == null) return;
            foreach (var header in headers)
            {
                if (Matches(header.Key, DiagnosticHeaderHints))
                {
                    parts.Add($"{header.Key}={string.Join(',', header.Value)}");
                }
            }
        }
    }

    private static bool Matches(string name, string[] hints)
    {
        foreach (string hint in hints)
        {
            if (name.Contains(hint, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    // 读走正文后必须让它仍可再读:下游 ResponseSanitizer 与 OpenAI SDK 都还要各读一次,
    // 因此先整体缓冲再取。任何失败都不能影响这次响应本身,一律吞掉只放弃日志
    private static async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content == null) return [];
        try
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception e)
        {
            Log.Debug($"Read failed response body error: {e.Message}");
            return [];
        }
    }
}
