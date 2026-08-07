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

using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.LLM;

class OpenAICompatibleHttpHandler : DelegatingHandler
{
    private readonly Uri _baseUri;
    private readonly ILlmModel? _model;

    public OpenAICompatibleHttpHandler(ILlmModel? model, string address = "http://127.0.0.1:1369/v1/chat/completions")
        : base(new HttpClientHandler())
    {
        var newUriBuilder = CreateChatCompletionUri(address);
        _baseUri = newUriBuilder.Uri;
        _model = model;
    }

    public OpenAICompatibleHttpHandler(ILlmModel? model, string host = "http://127.0.0.1", int port = 1369,
        string absolutePath = "/v1/chat/completions")
        : base(new HttpClientHandler())
    {
        var newUriBuilder = new UriBuilder(host)
        {
            // Scheme = "http",
            // Host = host,
            Port = port,
            Path = absolutePath
        };
        _baseUri = newUriBuilder.Uri;
        _model = model;
    }

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

    // protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
    //     CancellationToken cancellationToken)
    // {
    //     request.RequestUri = _baseUri;
    //     // var mediaType = request.Content!.Headers!.ContentType!.MediaType;
    //     if (request.Content!.Headers!.ContentType!.MediaType == "application/json")
    //     {
    //         var content = Regex.Unescape(await request.Content!.ReadAsStringAsync(cancellationToken));
    //         Log.Debug($"OpenAI-compatible request: {content}");
    //         // request.Content = new StringContent(content, Encoding.UTF8, mediaType);
    //     }
    //
    //     return await base.SendAsync(request, cancellationToken);
    // }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // OpenAI SDK 会自行拼接标准路径，这里统一改写到用户配置的兼容端点。
        request.RequestUri = _baseUri;
        if (request.Method == HttpMethod.Post && request.Content != null)
        {
            var extraParams = _model?.GetExtraParams(LlmRequestContext.ThinkingMode);
            bool forbidToolCalls = LlmRequestContext.ForbidToolCalls;
            string? jsonContent = null;

            // 注入额外参数必须整体读出来重建 JSON,这一份读取是功能要求,躲不掉
            if (extraParams is { Count: > 0 } || forbidToolCalls)
            {
                jsonContent = await request.Content.ReadAsStringAsync(cancellationToken);
                var jsonNode = JsonNode.Parse(jsonContent)?.AsObject();

                if (jsonNode != null)
                {
                    if (extraParams != null)
                    {
                        foreach (var extraParam in extraParams)
                        {
                            jsonNode[extraParam.Key] = extraParam.Value;
                        }
                    }

                    // 带着工具定义(前缀缓存要对齐)但不许调用。MEAI 的 ChatToolMode 没有 None,
                    // 只能在这一层直接写进请求体
                    if (forbidToolCalls) jsonNode["tool_choice"] = "none";

                    jsonContent = jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                }
            }

            await LogRequestAsync(request, jsonContent, cancellationToken);
        }

        var response = await base.SendAsync(request, cancellationToken);
        await LogFailureAsync(response, cancellationToken);
        return await SanitizeResponseAsync(response, cancellationToken);
    }

    //兜底闸,正常内容够不着:抹掉 base64 之后还这么长的多半是出了别的岔子,不该让一条日志吃掉整个面板
    private const int LogSafetyLimit = 256 * 1024;
    private const int Base64RedactThreshold = 512; //比这短的 base64 留着,可能是真内容而不是附件

    /// <summary>
    /// 请求体日志。<b>不截断</b>——提示词、工具定义与参数都要能完整看到，
    /// 真正撑爆日志的从来不是正文长度而是内联的 base64 附件（一张图就十几 MB），
    /// 所以只把 base64 载荷换成一句体量说明，其余原样保留。
    ///
    /// 原先那道 <c>Regex.Unescape</c> 已去掉：它等于再复制一份，而且遇到非法转义序列会当场抛。
    /// </summary>
    /// <param name="request">请求</param>
    /// <param name="knownContent">已因注入额外参数而读出的正文；未读过时为 null</param>
    /// <param name="cancellationToken">取消标记</param>
    private static async Task LogRequestAsync(HttpRequestMessage request, string? knownContent,
        CancellationToken cancellationToken)
    {
        if (knownContent == null)
        {
            try
            {
                knownContent = await request.Content!.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception e)
            {
                Log.Debug($"Read request body for logging failed: {e.Message}");
                return;
            }
        }

        Log.Debug($"OpenAI-compatible request ({knownContent.Length:N0} chars): {ForLog(knownContent)}");
    }

    // data: URL 形式(MEAI 的 OpenAI 客户端就发这个),以及裸 base64 字符串值。
    // 正文里的自然语言必然带空格与标点,落不进 base64 字符集,因此这里不会误伤提示词
    private static readonly Regex DataUrlBase64 = new(
        $@"(data:[^"";\\]{{0,64}};base64,)[A-Za-z0-9+/=\s]{{{Base64RedactThreshold},}}",
        RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    private static readonly Regex BareBase64Value = new(
        $@"""[A-Za-z0-9+/=]{{{Base64RedactThreshold},}}""",
        RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    //缩进 + 不转义非 ASCII。后者取代了原先那道 Regex.Unescape:
    //不加的话中文会写成 \uXXXX,日志基本没法读——这是编码器该干的事,不该靠事后拿正则去还原
    private static readonly JsonSerializerOptions LogJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 把正文整理成可写进日志的形态：先抹掉 base64 载荷，再展开成缩进 JSON。
    ///
    /// 顺序不能反——先展开的话，那十几 MB 的 base64 会先被重新序列化一遍。
    /// 抹完之后正文通常只剩几 KB，展开的代价可以忽略。
    /// </summary>
    /// <param name="body">原始正文</param>
    /// <returns>可写进日志的文本</returns>
    internal static string ForLog(string body)
    {
        string text;
        try
        {
            text = DataUrlBase64.Replace(body, m => $"{m.Groups[1].Value}<{m.Length - m.Groups[1].Length} base64 chars>");
            text = BareBase64Value.Replace(text, m => $"\"<{m.Length - 2} base64 chars>\"");
        }
        catch (RegexMatchTimeoutException)
        {
            text = body; //抹不动就照原样,下面还有体量闸兜着
        }

        text = Prettify(text);
        return text.Length <= LogSafetyLimit
            ? text
            : string.Concat(text.AsSpan(0, LogSafetyLimit), $"…(+{text.Length - LogSafetyLimit:N0} chars)");
    }

    // 不是 JSON(或已被截断成半截)就原样返回:日志格式化失败不该影响任何事
    private static string Prettify(string text)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(text);
            return node?.ToJsonString(LogJsonOptions) ?? text;
        }
        catch (JsonException)
        {
            return text;
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
    /// <param name="cancellationToken">取消标记</param>
    private static async Task LogFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        string? diagnostics = FormatDiagnosticHeaders(response);
        string body = await ReadBodySnippetAsync(response, cancellationToken);
        StringBuilder sb = new($"OpenAI-compatible request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        if (diagnostics != null) sb.Append(" | ").Append(diagnostics);
        if (body.Length > 0) sb.Append('\n').Append(body);
        Log.Warning(sb.ToString());
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
    // 因此先整体缓冲再取字符串。任何失败都不能影响这次响应本身,一律吞掉只放弃日志
    private static async Task<string> ReadBodySnippetAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content == null) return string.Empty;
        try
        {
            await response.Content.LoadIntoBufferAsync();
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ForLog(body); //不压成单行:日志面板自己会做单行摘要+详情展开,压了反而看不了格式
        }
        catch (Exception e)
        {
            Log.Debug($"Read failed response body error: {e.Message}");
            return string.Empty;
        }
    }

    // 部分兼容服务(如商汤 Sensenova)会返回空的/非标准的 finish_reason，OpenAI SDK 解析枚举时会直接抛异常
    private async Task<HttpResponseMessage> SanitizeResponseAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!_baseUri.AbsolutePath.Contains("chat/completions", StringComparison.OrdinalIgnoreCase)) return response;

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType == "text/event-stream")
        {
            response.Content = new SseSanitizingContent(response.Content);
        }
        else if (mediaType == "application/json")
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);
            if (fixedJson != null)
            {
                var contentType = response.Content.Headers.ContentType;
                response.Content = new StringContent(fixedJson, Encoding.UTF8, "application/json");
                if (contentType != null) response.Content.Headers.ContentType = contentType;
            }
        }

        return response;
    }
}
