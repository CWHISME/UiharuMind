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
    private readonly Uri _baseUri;
    private readonly bool _isChatCompletions; //只有聊天补全的响应需要清洗
    private readonly Action<string>? _reasoningFieldObserver; //响应观测到思考字段原名时的回调(按模型记录回传键名)
    private readonly string[] _affinityHeaders; //本端点要写的会话亲和头名；空=不发

    public OpenAICompatibleHttpHandler(string address = "http://127.0.0.1:1369/v1/chat/completions",
        Action<string>? reasoningFieldObserver = null,
        IReadOnlyList<string>? affinityHeaders = null)
        : this(address, new HttpClientHandler(), reasoningFieldObserver, affinityHeaders)
    {
    }

    /// <summary>
    /// 指定内层处理器（测试用：替掉真实网络）
    /// </summary>
    /// <param name="address">端点地址</param>
    /// <param name="inner">内层处理器</param>
    /// <param name="reasoningFieldObserver">思考字段观测回调（可选，观测到 origin 字段名时调用）</param>
    /// <param name="affinityHeaders">会话亲和要写的头名；空或 null 表示这家网关不认，一个都不发</param>
    internal OpenAICompatibleHttpHandler(string address, HttpMessageHandler inner,
        Action<string>? reasoningFieldObserver = null,
        IReadOnlyList<string>? affinityHeaders = null) : base(inner)
    {
        _baseUri = CreateChatCompletionUri(address).Uri;
        _isChatCompletions = IsChatCompletions(_baseUri);
        _reasoningFieldObserver = reasoningFieldObserver;
        _affinityHeaders = affinityHeaders is { Count: > 0 } ? [.. affinityHeaders] : [];
    }

    public OpenAICompatibleHttpHandler(string host = "http://127.0.0.1", int port = 1369,
        string absolutePath = "/v1/chat/completions", Action<string>? reasoningFieldObserver = null,
        IReadOnlyList<string>? affinityHeaders = null)
        : this(new UriBuilder(host) { Port = port, Path = absolutePath }.Uri.ToString(), new HttpClientHandler(),
            reasoningFieldObserver, affinityHeaders)
    {
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
        // 非该媒体类型的流不经过 SseSanitizingStream,此类故障的桩也就全都不在链路上。
        // 顺带记下这一发的亲和:命中率不对时先回答"到底发了什么"——这三种情况的修法完全不同
        string affinity = AffinityForLog();
        Log.Debug($"OpenAI-compatible response: {(int)response.StatusCode} {response.ReasonPhrase}, " +
                  $"content-type: {response.Content?.Headers.ContentType?.MediaType ?? "(null)"}" +
                  (_isChatCompletions ? "" : " (not sanitized)") +
                  $", affinity: {affinity}",
            ELogCategory.LlmResponse);
        await LogFailureAsync(response, request.RequestUri, cancellationToken);
        return await SanitizeResponseAsync(response, cancellationToken);
    }

    /// <summary>
    /// 这一发的亲和<b>到底发出去了什么</b>，三种情况分得清——命中率不对时，它们的修法完全不同：
    /// <c>(off)</c> 这家网关没声明头名（默认如此），一个都没发，别去查网关为什么不认；
    /// <c>(none)</c> 声明了但本次没有会话（能力预览），也发不出去；
    /// 否则是真正写到网线上的那个值。
    ///
    /// <b>不能只打 <see cref="LlmRequestContext.SessionAffinity"/></b>：那是「手上有的值」，
    /// 与「实际发了什么」不是一回事——没声明头名的供应商照样有值，打出来会让人以为发了。
    /// 三种取值都不带空格，好让 <c>cache-audit.py</c> 按整词分组。
    /// </summary>
    /// <returns>日志里要显示的亲和</returns>
    private string AffinityForLog()
    {
        if (_affinityHeaders.Length == 0) return "(off)";
        return LlmRequestContext.SessionAffinity ?? "(none)";
    }

    /// <summary>
    /// 写会话亲和头：同一会话（含工具往返、旁路请求）的请求带同一个值，网关据此把它们钉到同一台副本——
    /// 前缀缓存按副本各存一份，落到没建过该前缀的副本上就是整段冷启动，命中率看着像坏了，
    /// 其实是另一台机器在答。
    ///
    /// 头名由供应商声明（<see cref="ILlmModel.SessionAffinityHeaders"/>），这里只照单写；
    /// 值恒为 32 个十六进制字符（<see cref="SessionAffinity.Of"/>），不需要在发请求这层校验。
    /// <b>不动请求体</b>：头不参与前缀缓存，加它不会让任何一份已有缓存失效。
    /// </summary>
    /// <param name="request">待发出的请求</param>
    private void ApplySessionAffinity(HttpRequestMessage request)
    {
        if (_affinityHeaders.Length == 0) return;
        string? affinity = LlmRequestContext.SessionAffinity;
        if (string.IsNullOrEmpty(affinity)) return;

        foreach (string header in _affinityHeaders)
        {
            request.Headers.TryAddWithoutValidation(header, affinity);
        }
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
