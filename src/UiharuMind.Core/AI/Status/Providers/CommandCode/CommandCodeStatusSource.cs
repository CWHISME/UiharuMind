using System.Net;
using System.Net.Http.Headers;

namespace UiharuMind.Core.AI.Status.Providers.CommandCode;

/// <summary>
/// CC 的账户级状态：<c>/alpha/billing/credits</c>（5 小时 / 周窗口 + 余额）与
/// <c>/alpha/whoami</c>（计划名）。
///
/// <b>不复用聊天那条 <c>OpenAICompatibleHttpHandler</c></b>：它构造时就把 URI 改写成配置的
/// chat/completions 端点（响应清洗、思考字段观测也都在那条链上），拿它发状态请求必然打到错误路径。
/// 这里用独立的 <see cref="HttpClient"/>，8 秒超时。
///
/// 端点与字段都是未公开的第三方接口，<b>会变</b>：URL 常量只在本类里，解析容错在
/// <see cref="CommandCodeStatusParser"/>，整份读不出结构时返回 null（顶栏整块不显示，不影响聊天）。
/// </summary>
public sealed class CommandCodeStatusSource : IProviderStatusSource
{
    /// <summary>界面上显示的供应商名</summary>
    internal const string ProviderName = "Command Code";

    private const string CreditsPath = "/alpha/billing/credits";
    private const string WhoamiPath = "/alpha/whoami";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _http;

    /// <summary>
    /// 生产用：独立 HttpClient，8 秒超时
    /// </summary>
    public CommandCodeStatusSource() : this(new HttpClientHandler())
    {
    }

    /// <summary>
    /// 测试用：替掉真实网络
    /// </summary>
    /// <param name="handler">内层处理器</param>
    internal CommandCodeStatusSource(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler) { Timeout = RequestTimeout };
    }

    /// <summary>
    /// 取一次账户级状态；凭据失效转成降级快照，其余失败抛出去让服务层退避
    /// </summary>
    /// <param name="context">取数输入</param>
    /// <param name="ct">取消</param>
    /// <returns>快照；读不出结构时 null</returns>
    public async Task<ProviderStatusSnapshot?> FetchAsync(ProviderStatusContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.ApiBaseUrl) || string.IsNullOrWhiteSpace(context.ApiKey)) return null;

        DateTimeOffset now = DateTimeOffset.Now;
        HttpResponseMessage creditsResponse = await _http.SendAsync(Build(context, CreditsPath), ct)
            .ConfigureAwait(false);
        using (creditsResponse)
        {
            if (creditsResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // 用户必须知道 key 坏了，所以这不是「没数据」而是「有结论」
                return new ProviderStatusSnapshot(ProviderName, [], null,
                    [new ProviderStatusNotice(EProviderNoticeKind.CredentialInvalid,
                        Detail: ((int)creditsResponse.StatusCode).ToString())], now);
            }

            // 429 是状态接口自己的限流、5xx 是它抽风：都不是「额度打满」，抛给服务层退避
            if (!creditsResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"{CommandCodeStatusSource.ProviderName} status endpoint returned {(int)creditsResponse.StatusCode}");
            }

            string creditsJson = await creditsResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string? whoamiJson = await TryWhoamiAsync(context, ct).ConfigureAwait(false);
            return CommandCodeStatusParser.Parse(creditsJson, whoamiJson, now);
        }
    }

    private async Task<string?> TryWhoamiAsync(ProviderStatusContext context, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(Build(context, WhoamiPath), ct)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)
                : null;
        }
        catch (Exception)
        {
            // 计划名是锦上添花，拿不到就当没有
            return null;
        }
    }

    private static HttpRequestMessage Build(ProviderStatusContext context, string path)
    {
        HttpRequestMessage request = new(HttpMethod.Get, context.ApiBaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }
}
