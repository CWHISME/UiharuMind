using System.Net.Http.Headers;

namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>模型目录的网络拉取契约</summary>
public interface IModelCatalogFetcher
{
    /// <summary>拉取服务商 /v1/models 目录；有 key 时带 Bearer（目录公开时可不填）</summary>
    Task<IReadOnlyList<ModelCatalogEntry>> FetchAsync(string catalogUrl, string? apiKey,
        CancellationToken cancellationToken = default);
}

/// <summary>模型目录的 HTTP 拉取器：GET /v1/models → 解析成候选</summary>
public sealed class ModelCatalogFetcher : IModelCatalogFetcher
{
    // 共享静态 HttpClient：编辑窗口反复开关不该堆积 socket 句柄
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<IReadOnlyList<ModelCatalogEntry>> FetchAsync(string catalogUrl, string? apiKey,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, catalogUrl);
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ModelCatalogJsonParser.Parse(json);
    }
}
