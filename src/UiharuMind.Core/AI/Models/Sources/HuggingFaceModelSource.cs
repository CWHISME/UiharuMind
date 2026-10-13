using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.AI.Models.Sources;

/// <summary>
/// HuggingFace 及与其接口兼容的镜像（hf-mirror 等），镜像只换地址不换协议
/// </summary>
/// <param name="httpClient">HTTP 客户端</param>
/// <param name="endpoint">站点根地址，如 https://huggingface.co</param>
/// <param name="token">访问令牌（受限仓库要），镜像也照发</param>
public sealed partial class HuggingFaceModelSource(HttpClient httpClient, string endpoint, string? token) : IModelSource
{
    public const string SourceId = "huggingface";
    private const string Revision = "main";

    private readonly string _endpoint = endpoint.TrimEnd('/');

    public string Id => SourceId;

    public async Task<ModelSearchPage> SearchAsync(string query, EModelSearchSort sort, int limit, string? cursor,
        CancellationToken cancellationToken)
    {
        // 默认字段里没有 lastModified，要 expand；一旦 expand 就只回列出的字段（id 总在）
        string url = $"{_endpoint}/api/models?search={Uri.EscapeDataString(query)}&filter=gguf&sort={SortKey(sort)}" +
                     $"&limit={limit}&expand[]=downloads&expand[]=likes&expand[]=lastModified&expand[]=createdAt";
        if (!string.IsNullOrEmpty(cursor)) url += $"&cursor={cursor}";
        using HttpResponseMessage response = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        List<SearchItem>? items = await JsonSerializer.DeserializeAsync(stream, HuggingFaceJsonContext.Default.ListSearchItem,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new ModelSearchPage(
            items?.Where(x => !string.IsNullOrEmpty(x.Id))
                .Select(x => new ModelRepoSummary(x.Id!, x.Downloads, x.Likes, x.LastModified, x.CreatedAt)).ToList() ?? [],
            ReadNextCursor(response));
    }

    private static string SortKey(EModelSearchSort sort) => sort switch
    {
        EModelSearchSort.Trending => "trendingScore",
        EModelSearchSort.Likes => "likes",
        EModelSearchSort.Updated => "lastModified",
        EModelSearchSort.Created => "createdAt",
        _ => "downloads"
    };

    // 从 Link 头的 rel="next" 里取 cursor 参数（保持转义原样）。镜像会把接口重定向回官方，只取游标、地址自己拼
    private static string? ReadNextCursor(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? values)) return null;
        foreach (string link in values.SelectMany(x => x.Split(',')))
        {
            if (!link.Contains("rel=\"next\"", StringComparison.Ordinal)) continue;
            int start = link.IndexOf('<');
            int end = link.IndexOf('>', start + 1);
            if (start < 0 || end < 0 || !Uri.TryCreate(link[(start + 1)..end], UriKind.Absolute, out Uri? uri)) continue;
            foreach (string pair in uri.Query.TrimStart('?').Split('&'))
                if (pair.StartsWith("cursor=", StringComparison.Ordinal) && pair.Length > "cursor=".Length)
                    return pair["cursor=".Length..];
        }

        return null;
    }

    public async Task<IReadOnlyList<ModelRepoFile>> ListFilesAsync(string repository,
        CancellationToken cancellationToken, string? revision = null)
    {
        string url = $"{_endpoint}/api/models/{repository}/tree/{Uri.EscapeDataString(revision ?? Revision)}?recursive=true";
        List<TreeItem>? items = await GetJsonAsync(url, HuggingFaceJsonContext.Default.ListTreeItem, cancellationToken).ConfigureAwait(false);
        return items?.Where(x => x.Type == "file" && !string.IsNullOrEmpty(x.Path))
            .Select(x => new ModelRepoFile(x.Path!, x.Lfs?.Size ?? x.Size, x.Lfs?.Oid))
            .ToList() ?? [];
    }

    public Task<string?> GetReadmeAsync(string repository, CancellationToken cancellationToken) =>
        ModelReadme.FetchAsync(httpClient, $"{_endpoint}/{repository}/resolve/{Revision}/README.md", AuthHeaders(),
            cancellationToken);

    public DownloadRequest CreateDownload(string repository, ModelRepoFile file, string destinationPath,
        string? revision = null)
    {
        string path = string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString));
        return new DownloadRequest(
            new Uri($"{_endpoint}/{repository}/resolve/{Uri.EscapeDataString(revision ?? Revision)}/{path}"), destinationPath,
            1, file.Sha256, AuthHeaders());
    }

    private Dictionary<string, string>? AuthHeaders() =>
        string.IsNullOrWhiteSpace(token) ? null : new() { ["Authorization"] = $"Bearer {token}" };

    private async Task<T?> GetJsonAsync<T>(string url, JsonTypeInfo<T> typeInfo, CancellationToken token)
    {
        using HttpResponseMessage response = await SendAsync(url, token).ConfigureAwait(false);
        await using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, token).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        foreach ((string key, string value) in AuthHeaders() ?? [])
            request.Headers.TryAddWithoutValidation(key, value);
        HttpResponseMessage response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private sealed class SearchItem
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("downloads")] public long Downloads { get; set; }
        [JsonPropertyName("likes")] public long Likes { get; set; }
        [JsonPropertyName("lastModified")] public DateTimeOffset? LastModified { get; set; }
        [JsonPropertyName("createdAt")] public DateTimeOffset? CreatedAt { get; set; }
    }

    private sealed class TreeItem
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("lfs")] public LfsInfo? Lfs { get; set; }
    }

    // LFS 文件的 oid 就是内容的 sha256
    private sealed class LfsInfo
    {
        [JsonPropertyName("oid")] public string? Oid { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    [JsonSerializable(typeof(List<SearchItem>))]
    [JsonSerializable(typeof(List<TreeItem>))]
    private sealed partial class HuggingFaceJsonContext : JsonSerializerContext;
}
