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

    public OpenAICompatibleHttpHandler(string address = "http://127.0.0.1:1369/v1/chat/completions")
        : this(address, new HttpClientHandler())
    {
    }

    /// <summary>
    /// 指定内层处理器（测试用：替掉真实网络）
    /// </summary>
    /// <param name="address">端点地址</param>
    /// <param name="inner">内层处理器</param>
    internal OpenAICompatibleHttpHandler(string address, HttpMessageHandler inner) : base(inner)
    {
        _baseUri = CreateChatCompletionUri(address).Uri;
        _isChatCompletions = IsChatCompletions(_baseUri);
    }

    public OpenAICompatibleHttpHandler(string host = "http://127.0.0.1", int port = 1369,
        string absolutePath = "/v1/chat/completions")
        : base(new HttpClientHandler())
    {
        var newUriBuilder = new UriBuilder(host)
        {
            Port = port,
            Path = absolutePath
        };
        _baseUri = newUriBuilder.Uri;
        _isChatCompletions = IsChatCompletions(_baseUri);
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
        var response = await base.SendAsync(request, cancellationToken);
        // 诊断:无条件记录响应媒体类型与状态码——SseSanitizingContent 只在 text/event-stream 时介入,
        // 非该媒体类型的流不经过 SseSanitizingStream,此类故障的桩也就全都不在链路上
        Log.Debug($"OpenAI-compatible response: {(int)response.StatusCode} {response.ReasonPhrase}, " +
                  $"content-type: {response.Content?.Headers.ContentType?.MediaType ?? "(null)"}" +
                  (_isChatCompletions ? "" : " (not sanitized)"),
            ELogCategory.LlmResponse);
        await LogFailureAsync(response, cancellationToken);
        return await SanitizeResponseAsync(response, cancellationToken);
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
    /// <param name="cancellationToken">取消标记</param>
    private static async Task LogFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        string? diagnostics = FormatDiagnosticHeaders(response);
        string head = $"OpenAI-compatible request failed: {(int)response.StatusCode} {response.ReasonPhrase}";
        if (diagnostics != null) head += " | " + diagnostics;

        byte[] body = await ReadBodyAsync(response, cancellationToken);
        if (body.Length == 0)
        {
            Log.Warning(head, ELogCategory.LlmResponse);
            return;
        }

        //不压成单行:日志面板自己会做单行摘要+详情展开,压了反而看不了格式
        using PooledByteWriter formatted = new(body.Length + body.Length / 4);
        LlmBodyLogFormat.Format(body, formatted);
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
            response.Content = new SseSanitizingContent(response.Content);
        }
        else if (mediaType == "application/json")
        {
            HttpContent original = response.Content;
            byte[] json = await original.ReadAsByteArrayAsync(cancellationToken);
            var fixedJson = new ArrayBufferWriter<byte>(json.Length + 64);
            if (OpenAiCompatibleResponseFixer.TryFix(json, fixedJson))
            {
                response.Content = new ReadOnlyMemoryContent(fixedJson.WrittenMemory);
                response.Content.Headers.ContentType = original.Headers.ContentType;
                original.Dispose();
            }
        }

        return response;
    }
}
