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
            var extraParams = _model?.GetExtraParams();
            bool forbidToolCalls = LlmRequestContext.ForbidToolCalls;
            // 只对已确认要求 reasoning_content 回填的模型(目前只有 DeepSeek)生效,
            // 其余共用 thinking/reasoning_effort 参数的兼容服务不无谓塞多余字段
            var reasoningByCallId = _model?.RequiresReasoningContentRoundtrip == true
                ? LlmRequestContext.PendingReasoningByCallId
                : null;
            // 采样参数固定的模型(如 Kimi)会拒绝显式传值的请求,开启后删掉这四个字段,不碰其它参数
            bool omitSamplingParams = _model?.OmitSamplingParams == true;
            // 正文无论如何都要读:下面几道改写要它,日志也要它。
            // 曾经按条件延后读,结果是畸形参数扫描只在「不注入额外参数」的分支里做,
            // 一开思考模式这道修复就静默失效——恰恰是最需要它的路径
            string jsonContent = await request.Content.ReadAsStringAsync(cancellationToken);

            // 大多数请求不含畸形 tool_calls 参数,先做一次廉价子串扫描,避免每次都解析 JSON
            bool needsArgFix = jsonContent.Contains("\"arguments\":\"null\"") ||
                               jsonContent.Contains("\"arguments\": \"null\"");

            // 注入额外参数/修复畸形参数/回填思考正文都必须解析成 JSON 重建
            if (extraParams is { Count: > 0 } || forbidToolCalls || needsArgFix || reasoningByCallId is { Count: > 0 } ||
                omitSamplingParams)
            {
                var jsonNode = JsonNode.Parse(jsonContent)?.AsObject();

                if (jsonNode != null)
                {
                    if (omitSamplingParams) StripSamplingParams(jsonNode);

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

                    if (needsArgFix) SanitizeMalformedToolCallArguments(jsonNode);

                    if (reasoningByCallId is { Count: > 0 }) RestoreReasoningContent(jsonNode, reasoningByCallId);

                    jsonContent = jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                }
            }

            LogRequest(jsonContent);
        }

        var response = await base.SendAsync(request, cancellationToken);
        // 诊断:无条件记录响应媒体类型与状态码——SseSanitizingContent 只在 text/event-stream 时介入,
        // 非该媒体类型的流不经过 SseSanitizingStream,此类故障的桩也就全都不在链路上
        Log.Debug($"OpenAI-compatible response: {(int)response.StatusCode} {response.ReasonPhrase}, " +
                  $"content-type: {response.Content?.Headers.ContentType?.MediaType ?? "(null)"}",
            ELogCategory.LlmResponse);
        await LogFailureAsync(response, cancellationToken);
        return await SanitizeResponseAsync(response, cancellationToken);
    }

    /// <summary>
    /// 模型偶发地把无参调用的 arguments 序列化成字面字符串 "null"(而非空对象 "{}")。
    /// 部分 OpenAI 兼容后端会对历史里的 arguments 做 json.loads 后 .items(),
    /// 解析出 None 就直接 400——'NoneType' object has no attribute 'items'。
    /// 修的是发出去的历史,不影响这次调用本身的执行结果。
    /// </summary>
    /// <param name="jsonNode">请求体根对象</param>
    private static void SanitizeMalformedToolCallArguments(JsonObject jsonNode)
    {
        if (jsonNode["messages"] is not JsonArray messages) return;

        foreach (var message in messages)
        {
            if (message?["tool_calls"] is not JsonArray toolCalls) continue;

            foreach (var toolCall in toolCalls)
            {
                if (toolCall?["function"] is not JsonObject function) continue;
                if (function["arguments"] is JsonValue value &&
                    value.TryGetValue(out string? arguments) && arguments == "null")
                {
                    function["arguments"] = "{}";
                }
            }
        }
    }

    /// <summary>
    /// 思考模式下,助手消息带 tool_calls 时接口要求原样带回当时的 reasoning_content,
    /// 否则报 "If thinking mode and tool_calls, reasoning_content must be passed back to the API"。
    /// 标准 ChatMessage→wire 消息转换认不出 <c>TextReasoningContent</c>,序列化时会把它悄悄丢掉,
    /// 只能按 tool_call id 从 <see cref="LlmRequestContext.PendingReasoningByCallId"/> 找回来补上。
    /// 一条消息可带多个 tool_calls,任一 id 命中即恢复整条消息的思考正文。
    /// </summary>
    /// <param name="jsonNode">请求体根对象</param>
    /// <param name="reasoningByCallId">本次请求历史里,按 tool_call id 索引的思考正文</param>
    internal static void RestoreReasoningContent(JsonObject jsonNode,
        IReadOnlyDictionary<string, string> reasoningByCallId)
    {
        if (jsonNode["messages"] is not JsonArray messages) return;

        foreach (var message in messages)
        {
            if (message is not JsonObject messageObj) continue;
            if (messageObj["reasoning_content"] != null) continue; // 已经带了,不覆盖
            if (messageObj["tool_calls"] is not JsonArray { Count: > 0 } toolCalls) continue;

            string? reasoningText = null;
            foreach (var toolCall in toolCalls)
            {
                if (toolCall is not JsonObject call) continue;
                if (call["id"] is JsonValue idValue &&
                    idValue.TryGetValue(out string? callId) &&
                    reasoningByCallId.TryGetValue(callId, out string? text))
                {
                    reasoningText = text;
                    break;
                }
            }

            if (reasoningText != null) messageObj["reasoning_content"] = reasoningText;
        }
    }

    /// <summary>
    /// 开启 <c>OmitSamplingParams</c> 的模型要删掉的采样参数。只删这四个,
    /// <c>max_tokens/thinking/tool_choice</c> 等不受影响。
    /// </summary>
    internal static readonly string[] SamplingParamKeys =
        ["temperature", "top_p", "presence_penalty", "frequency_penalty"];

    /// <summary>
    /// 删掉请求体里的采样参数,供采样参数固定的模型(如 Kimi)使用——
    /// 这类服务端对显式传值直接 400,只能不发,让服务端用默认值。
    /// </summary>
    /// <param name="jsonNode">请求体根对象</param>
    internal static void StripSamplingParams(JsonObject jsonNode)
    {
        foreach (string key in SamplingParamKeys)
            jsonNode.Remove(key);
    }

    private const int Base64RedactThreshold = 512; //比这短的 base64 留着,可能是真内容而不是附件

    /// <summary>
    /// 请求体日志。<b>完全不截断</b>——提示词、工具定义与参数都要能完整看到。
    /// 超过阈值的正文由日志层外置到 <c>Bodies.txt</c>，面板只吃索引，
    /// 因此这里再没有「一条日志吃掉整个面板」的问题（曾经那道 256KB 兜底闸已随之取消）。
    ///
    /// 仍然抹 base64：那不是截断，是把毫无阅读价值的附件载荷（一张图就十几 MB）
    /// 换成一句体量说明。
    /// </summary>
    /// <param name="content">实际发出的正文</param>
    private static void LogRequest(string content)
    {
        Log.Debug($"OpenAI-compatible request ({content.Length:N0} chars): {ForLog(content)}",
            ELogCategory.LlmRequest);
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
    ///
    /// <b>不做任何长度截断</b>：磁盘上永不截断，截断只发生在面板的列表行。
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

        return Prettify(text);
    }

    // 不是 JSON 就原样返回:日志格式化失败不该影响任何事
    private static string Prettify(string text)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(text);
            if (node == null) return text;
            return UnescapeJsonStringNewlines(node.ToJsonString(LogJsonOptions) ?? text);
        }
        catch (JsonException)
        {
            return text;
        }
    }

    // 字符串值里的换行被序列化器转义成字面 \n,日志里连成两行中间夹个反斜杠很难看。
    // WriteIndented 展开后,结构性的换行是<b>真换行</b>,字面 \n 只可能出现在字符串值内部,
    // 因此顺着 JSON 字符串扫描,把字符串值里的 \n 还原成真换行。不进字符串的结构换行不动。
    private static string UnescapeJsonStringNewlines(string pretty)
    {
        if (!pretty.Contains(@"\n", StringComparison.Ordinal)) return pretty;

        StringBuilder sb = new(pretty.Length);
        bool inString = false;
        for (int i = 0; i < pretty.Length; i++)
        {
            char c = pretty[i];

            if (inString)
            {
                if (c == '"')
                {
                    inString = false;
                    sb.Append(c);
                }
                else if (c == '\\' && i + 1 < pretty.Length && pretty[i + 1] == 'n')
                {
                    sb.Append('\n');
                    i++; //吞掉后面的 n
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"') inString = true;
                sb.Append(c);
            }
        }

        return sb.ToString();
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
        Log.Warning(sb.ToString(), ELogCategory.LlmResponse);
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
        Log.Debug($"SanitizeResponse: mediaType='{mediaType ?? "(null)"}'", ELogCategory.LlmResponse);
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
