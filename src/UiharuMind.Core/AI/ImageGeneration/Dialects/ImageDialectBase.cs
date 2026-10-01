using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UiharuMind.Core.AI.ImageGeneration.Dialects;

/// <summary>
/// 一种接口格式：把统一的出图请求翻成某家的 HTTP 请求，并把结果归成 <see cref="ImageOutcome"/>
/// </summary>
public interface IImageDialect
{
    /// <summary>
    /// 问一次
    /// </summary>
    /// <param name="model">生图模型</param>
    /// <param name="request">出图请求</param>
    /// <param name="ct">取消令牌；用户取消照常抛出，超时归为可能已扣费</param>
    /// <returns>结果，失败不抛异常</returns>
    Task<ImageOutcome> GenerateAsync(ImageModelInfo model, ImageRequest request, CancellationToken ct);
}

/// <summary>
/// 接口格式的公共骨架：发送、超时、失败分类、响应解析都在这里，
/// 派生类只回答「请求长什么样」。各家响应都是 OpenAI 那套 <c>data[].url / b64_json</c>，差别全在请求。
/// </summary>
internal abstract class ImageDialectBase : IImageDialect
{
    private const int MaxErrorChars = 500;

    private readonly HttpClient _http;

    protected ImageDialectBase(HttpClient http)
    {
        _http = http;
    }

    public async Task<ImageOutcome> GenerateAsync(ImageModelInfo model, ImageRequest request, CancellationToken ct)
    {
        // 私有参数写坏了是这一家的配置问题，换下一家
        if (!model.TryParseExtraBody(out JsonObject extra, out string? error))
        {
            return ImageOutcome.Fail(EImageFailureKind.Unavailable, error!);
        }

        using HttpRequestMessage message = BuildRequest(model, request, extra);
        if (!string.IsNullOrEmpty(model.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.ApiKey);
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, model.TimeoutSeconds)));
        try
        {
            using HttpResponseMessage response =
                await _http.SendAsync(message, timeout.Token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? ParseResponse(body)
                : ImageOutcome.Fail(Classify(response.StatusCode), DescribeError(response.StatusCode, body));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ImageOutcome.Fail(EImageFailureKind.MaybeCharged,
                $"timed out after {model.TimeoutSeconds}s; the request may already have been billed");
        }
        catch (HttpRequestException e) when (IsNotDelivered(e))
        {
            return ImageOutcome.Fail(EImageFailureKind.Unavailable, $"cannot reach the service: {e.Message}");
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            // 请求已经发出去，响应半路断了：服务端可能已经出完图、扣完费
            return ImageOutcome.Fail(EImageFailureKind.MaybeCharged,
                $"connection lost before the response completed; the request may already have been billed: {e.Message}");
        }
    }

    /// <summary>
    /// 构造这一家的请求
    /// </summary>
    /// <param name="model">生图模型</param>
    /// <param name="request">出图请求</param>
    /// <param name="extra">已解析的私有参数，同名键覆盖我们的取值</param>
    /// <returns>HTTP 请求（不含鉴权头）</returns>
    internal abstract HttpRequestMessage BuildRequest(ImageModelInfo model, ImageRequest request, JsonObject extra);

    /// <summary>
    /// 按状态码分类。400 一律归「请求本身不行」：SenseNova 的 <c>failed_precondition_error</c>
    /// 同时覆盖引擎不可用与安全检查未通过，分不清时宁可少换家也不重复扣费（ADR 0052）。
    /// 504 不算普通 5xx：网关等不及，上游多半已经在画、可能已扣费，换家就是再付一次
    /// </summary>
    /// <param name="status">HTTP 状态码</param>
    /// <returns>失败类别</returns>
    internal static EImageFailureKind Classify(HttpStatusCode status)
    {
        return status switch
        {
            HttpStatusCode.GatewayTimeout => EImageFailureKind.MaybeCharged,
            HttpStatusCode.Unauthorized or HttpStatusCode.PaymentRequired or HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests =>
                EImageFailureKind.Unavailable,
            >= HttpStatusCode.InternalServerError => EImageFailureKind.Unavailable,
            _ => EImageFailureKind.Rejected,
        };
    }

    /// <summary>
    /// 解析成功响应（<c>{"data":[{"url"|"b64_json", "revised_prompt"}]}</c>）
    /// </summary>
    /// <param name="json">响应正文</param>
    /// <returns>结果；成功却没有图归为可能已扣费</returns>
    internal static ImageOutcome ParseResponse(string json)
    {
        List<ImageOutput> images = new();
        string? revisedPrompt = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("data", out JsonElement data) &&
                data.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in data.EnumerateArray())
                {
                    string? base64 = GetString(item, "b64_json");
                    string? url = GetString(item, "url");
                    if (base64 != null) images.Add(new ImageOutput(Convert.FromBase64String(base64), null));
                    else if (url != null) images.Add(new ImageOutput(null, url));
                    revisedPrompt ??= GetString(item, "revised_prompt");
                }
            }
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            return ImageOutcome.Fail(EImageFailureKind.MaybeCharged, $"unreadable response: {e.Message}");
        }

        return images.Count == 0
            ? ImageOutcome.Fail(EImageFailureKind.MaybeCharged, "the service reported success but returned no image")
            : new ImageOutcome { Images = images, RevisedPrompt = revisedPrompt };
    }

    /// <summary>
    /// 拼接口地址。用户填服务根（<c>…/v1</c>）或整条接口地址都认
    /// </summary>
    /// <param name="endpoint">配置里的地址</param>
    /// <param name="path">接口路径，如 <c>/images/generations</c></param>
    /// <returns>完整地址</returns>
    internal static Uri ResolveUri(string endpoint, string path)
    {
        string root = endpoint.Trim().TrimEnd('/');
        foreach (string suffix in (string[])["/images/generations", "/images/edits"])
        {
            if (root.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) root = root[..^suffix.Length];
        }

        return new Uri(root + path);
    }

    /// <summary>
    /// 生成时比例缺省为 1:1；编辑时缺省为 null，交给各家「跟主图」
    /// </summary>
    /// <param name="request">出图请求</param>
    /// <returns>生效的比例</returns>
    protected static ImageAspectRatio? EffectiveRatio(ImageRequest request) =>
        request.AspectRatio ?? (request.IsEdit ? null : ImageAspectRatio.Square);

    /// <summary>
    /// 构造 JSON 请求，私有参数并进顶层
    /// </summary>
    /// <param name="uri">接口地址</param>
    /// <param name="body">请求正文</param>
    /// <param name="extra">私有参数</param>
    /// <returns>HTTP 请求</returns>
    protected static HttpRequestMessage JsonRequest(Uri uri, JsonObject body, JsonObject extra)
    {
        foreach ((string key, JsonNode? value) in extra) body[key] = value?.DeepClone();
        return new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    // 没送到服务端的几种：这一家此刻不可达，换家不会重复扣费
    private static bool IsNotDelivered(HttpRequestException e) =>
        e.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError or HttpRequestError.ProxyTunnelError;

    private static string DescribeError(HttpStatusCode status, string body)
    {
        string detail = body.Length > MaxErrorChars ? body[..MaxErrorChars] + "…" : body;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            JsonElement error = root.TryGetProperty("error", out JsonElement nested) &&
                                nested.ValueKind == JsonValueKind.Object
                ? nested
                : root;
            string? message = GetString(error, "message");
            if (message != null)
            {
                string? type = GetString(error, "type") ?? GetString(error, "code");
                detail = type != null ? $"{type}: {message}" : message;
            }
        }
        catch (JsonException)
        {
            // 不是 JSON（网关的 HTML 错误页之类）：原文截断即可
        }

        return $"HTTP {(int)status} {detail}".TrimEnd();
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;
    }
}
