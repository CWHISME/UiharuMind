/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Core.LLM;

/// <summary>
/// OpenAI 兼容端点的 HTTP 层：把 SDK 拼的标准路径改写到配置的端点，记录响应、清洗不规范的响应。
/// 请求体的改写与日志不在这里，在 <see cref="OpenAICompatibleRequestPolicy"/>（重试之前，每次调用只做一次）。
/// </summary>
class OpenAICompatibleHttpHandler : DelegatingHandler
{
    /// <summary>「亲和值不合规」这条日志的节流间隔：坏值会每发都来，不节流就是刷屏</summary>
    private static readonly long UnsafeAffinityReportInterval = 60_000;

    private static long _lastUnsafeAffinityReportAt; //上次记这条日志的时刻(Environment.TickCount64)

    private readonly Uri _baseUri;
    private readonly bool _isChatCompletions; //只有聊天补全的响应需要清洗
    private readonly Action<string>? _reasoningFieldObserver; //响应观测到思考字段原名时的回调(按模型记录回传键名)

    public OpenAICompatibleHttpHandler(string address = "http://127.0.0.1:1369/v1/chat/completions",
        Action<string>? reasoningFieldObserver = null)
        : this(address, new HttpClientHandler(), reasoningFieldObserver)
    {
    }

    /// <summary>
    /// 指定内层处理器（测试用：替掉真实网络）
    /// </summary>
    /// <param name="address">端点地址</param>
    /// <param name="inner">内层处理器</param>
    /// <param name="reasoningFieldObserver">思考字段观测回调（可选，观测到 origin 字段名时调用）</param>
    internal OpenAICompatibleHttpHandler(string address, HttpMessageHandler inner,
        Action<string>? reasoningFieldObserver = null) : base(inner)
    {
        _baseUri = CreateChatCompletionUri(address).Uri;
        _isChatCompletions = IsChatCompletions(_baseUri);
        _reasoningFieldObserver = reasoningFieldObserver;
    }

    public OpenAICompatibleHttpHandler(string host = "http://127.0.0.1", int port = 1369,
        string absolutePath = "/v1/chat/completions", Action<string>? reasoningFieldObserver = null)
        : base(new HttpClientHandler())
    {
        var newUriBuilder = new UriBuilder(host)
        {
            Port = port,
            Path = absolutePath
        };
        _baseUri = newUriBuilder.Uri;
        _isChatCompletions = IsChatCompletions(_baseUri);
        _reasoningFieldObserver = reasoningFieldObserver;
    }

    private static bool IsChatCompletions(Uri uri) =>
        uri.AbsolutePath.Contains("chat/completions", StringComparison.OrdinalIgnoreCase);

    private static UriBuilder CreateChatCompletionUri(string address)
    {
        var builder = new UriBuilder(address);
        string path = builder.Path.TrimEnd('/');
        // 远程配置既允许填写完整接口，也允许只填写 OpenAI-compatible 的服务根路径。
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            builder.Path = path + "/chat/completions";
        else if (string.IsNullOrEmpty(path))
            builder.Path = "/v1/chat/completions";
        return builder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // OpenAI SDK 会自行拼接标准路径，这里统一改写到用户配置的兼容端点。
        request.RequestUri = _baseUri;
        ApplySessionAffinity(request);
        var response = await base.SendAsync(request, cancellationToken);
        // 诊断:无条件记录响应媒体类型与状态码——SseSanitizingContent 只在 text/event-stream 时介入,
        // 非该媒体类型的流不经过 SseSanitizingStream,此类故障的桩也就全都不在链路上
        // 顺带记下这一发实际带出去的亲和值：命中率不对时,第一个要回答的问题是"到底发了什么"——
        // 不记下来只能靠猜(网关不认,还是我们压根没发),而这两者的修法完全不同
        string affinity = LlmRequestContext.SessionAffinity ?? "(none)";
        Log.Debug($"OpenAI-compatible response: {(int)response.StatusCode} {response.ReasonPhrase}, " +
                  $"content-type: {response.Content?.Headers.ContentType?.MediaType ?? "(null)"}" +
                  (_isChatCompletions ? "" : " (not sanitized)") +
                  $", affinity: {affinity}",
            ELogCategory.LlmResponse);
        await LogFailureAsync(response, request.RequestUri, cancellationToken);
        return await SanitizeResponseAsync(response, cancellationToken);
    }

    /// <summary>
    /// 写会话亲和头。同一会话（含工具往返、旁路请求）的请求带着同一个值，
    /// 网关据此把它们路由到同一后端——前缀缓存按副本各存一份，落到没建过该前缀的副本上
    /// 就是整段冷启动，命中率看着像坏了，其实是另一台机器在答。
    ///
    /// 这四条是 pi 三种亲和格式的并集（<c>sessionAffinityFormat</c>：<c>openai</c> 发前三条，
    /// <c>openai-nosession</c> 发后两条，<c>openrouter</c> 只发 <c>x-session-id</c>）：
    /// <c>x-client-request-id</c> 与官方 Codex CLI 同款，是最通用的一条；
    /// <c>x-session-affinity</c> 是 Fireworks / Vercel AI Gateway 这类按副本做前缀路由的网关认的；
    /// <c>session_id</c> 供只认下划线名的那一类，但 nginx 默认（<c>underscores_in_headers off</c>）
    /// 会把它丢掉，别把它当主通道；<c>x-session-id</c> 是 OpenRouter 认的那一条。
    /// 对不认识它们的网关就是未知头，直接忽略。
    ///
    /// <b>不动请求体</b>：头不参与前缀缓存，加它不会让任何一份已有缓存失效。
    /// <c>prompt_cache_key</c> 那种要写进 body 的会改字节、让已有缓存失效一次，因此不在这里做——
    /// 参考实现也都按模型开关：pi 只在 <c>api.openai.com</c> 或长缓存时发，
    /// opencode 要 <c>supportsPromptCacheKey</c> 显式打开（默认关，严格服务端会因未知字段 400）。
    /// 我们目前一个都不发，等价于 opencode 的默认；要开就得在模型配置上开一个口子，别全局加。
    /// </summary>
    /// <param name="request">待发出的请求</param>
    private static void ApplySessionAffinity(HttpRequestMessage request)
    {
        string? affinity = LlmRequestContext.SessionAffinity;
        if (string.IsNullOrEmpty(affinity)) return;
        if (!IsSafeHeaderValue(affinity))
        {
            ReportUnsafeAffinity(affinity);
            return;
        }

        request.Headers.TryAddWithoutValidation("session_id", affinity);
        request.Headers.TryAddWithoutValidation("x-client-request-id", affinity);
        request.Headers.TryAddWithoutValidation("x-session-affinity", affinity);
        request.Headers.TryAddWithoutValidation("x-session-id", affinity);
    }

    /// <summary>
    /// 亲和值能不能直接当请求头值：只用可见 ASCII 的 token 字符。
    ///
    /// 生产路径上的值恒为 32 个十六进制字符（<see cref="SessionAffinity.Of"/> 的输出），
    /// 所以这道检查防的不是数据，是<b>调用方写错</b>——<see cref="LlmRequestContext.SessionAffinity"/>
    /// 是公开可写的静态属性，谁要是图省事塞了标题或原始 id，一个 CR/LF 就会让
    /// <c>TryAddWithoutValidation</c>（按名字说的那样<b>不校验</b>）把请求在写网线那一步抛掉，
    /// 变成整个会话发不出请求。宁可少发一个头。
    ///
    /// <b>不合法就不发，而不是把字符剔掉</b>：这个值是<b>身份</b>不是文本。剔掉 <c>\r\n</c>
    /// 会造出一个从未存在过的标识，撞上另一个会话的值时两套前缀就被钉到同一台副本上——
    /// 串号比不带更糟；而且"剔到哪一步算合法"没有答案（<c>a\r\nb: c</c> 剔完还是非法）。
    /// </summary>
    /// <param name="value">亲和值（调用方已排除空值）</param>
    /// <returns>可以安全写入请求头时为 true</returns>
    private static bool IsSafeHeaderValue(string value)
    {
        if (value.Length > 128) return false;
        foreach (char ch in value)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_' or '.')) return false;
        }

        return true;
    }

    /// <summary>
    /// 记一条「亲和值不合规，这一发不带头」。节流到一分钟一条——坏值会每发都来，不节流就是刷屏；
    /// 值按 hex 打，因为它可能含控制字符，原样写进日志会把格式搅乱。
    /// </summary>
    /// <param name="value">不合规的亲和值</param>
    private static void ReportUnsafeAffinity(string value)
    {
        long now = Environment.TickCount64;
        long last = Interlocked.Read(ref _lastUnsafeAffinityReportAt);
        if (now - last < UnsafeAffinityReportInterval) return;
        if (Interlocked.CompareExchange(ref _lastUnsafeAffinityReportAt, now, last) != last) return;
        Log.Warning($"session affinity is not a valid header value (length {value.Length}, " +
                    $"hex {Convert.ToHexString(Encoding.UTF8.GetBytes(value))}); " +
                    "this request goes out without affinity headers.",
            ELogCategory.LlmRequest);
    }

    //各家名字都不一样(OpenAI 用 x-ratelimit-*,Anthropic 用 anthropic-ratelimit-*,
    //部分国内网关用 x-tc-requestid 之类),因此按子串命中而不是白名单精确匹配
    private static readonly string[] DiagnosticHeaderHints =
        ["ratelimit", "rate-limit", "retry-after", "request-id", "requestid"];
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
    private static async Task LogFailureAsync(HttpResponseMessage response, Uri? uri,
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
            Log.Warning($"{head}\n(response body could not be formatted: {e.GetType().Name}: {e.Message})", ELogCategory.LlmResponse);
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

    // 读走正文后必须让它仍可再读:下游 SanitizeResponseAsync 与 OpenAI SDK 都还要各读一次,
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

    // 部分兼容服务(如商汤 Sensenova)会返回空的/非标准的 finish_reason，OpenAI SDK 解析枚举时会直接抛异常
    private async Task<HttpResponseMessage> SanitizeResponseAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!_isChatCompletions) return response;

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType == "text/event-stream")
        {
            response.Content = new SseSanitizingContent(response.Content, _reasoningFieldObserver);
        }
        else if (mediaType == "application/json")
        {
            HttpContent original = response.Content;
            byte[] json = await original.ReadAsByteArrayAsync(cancellationToken);
            var fixedJson = new ArrayBufferWriter<byte>(json.Length + 64);
            if (OpenAiCompatibleResponseFixer.TryFix(json, fixedJson, out string? reasoningField))
            {
                response.Content = new ReadOnlyMemoryContent(fixedJson.WrittenMemory);
                response.Content.Headers.ContentType = original.Headers.ContentType;
                if (reasoningField != null) _reasoningFieldObserver?.Invoke(reasoningField);
                original.Dispose();
            }
        }

        return response;
    }
}
