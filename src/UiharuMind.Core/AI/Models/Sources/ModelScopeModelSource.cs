using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.AI.Models.Sources;

/// <summary>
/// ModelScope（魔搭）：与 HF 是两套接口，同名仓库也未必同一个东西
/// </summary>
/// <param name="httpClient">HTTP 客户端</param>
/// <param name="token">访问令牌，公开仓库不需要</param>
public sealed class ModelScopeModelSource(HttpClient httpClient, string? token) : IModelSource
{
    public const string SourceId = "modelscope";
    private const string Endpoint = "https://modelscope.cn";
    private const string Revision = "master";

    public string Id => SourceId;

    public async Task<IReadOnlyList<ModelRepoSummary>> SearchAsync(string query, int limit,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Put, $"{Endpoint}/api/v1/dolphin/models");
        // 魔搭搜索没有可用的 GGUF 过滤条件（libraries 条件会被忽略），关键词里带上 GGUF 才收得住
        string name = query.Contains("gguf", StringComparison.OrdinalIgnoreCase) ? query : $"{query} GGUF";
        request.Content = JsonContent.Create(new SearchRequest(limit, 1, "Default", "", [], name));
        SearchResponse? response = await SendAsync<SearchResponse>(request, cancellationToken).ConfigureAwait(false);
        return response?.Data?.Model?.Models?
            .Where(x => !string.IsNullOrEmpty(x.Path) && !string.IsNullOrEmpty(x.Name))
            .Select(x => new ModelRepoSummary($"{x.Path}/{x.Name}", x.Downloads))
            .OrderByDescending(x => x.Downloads) //魔搭的默认排序不是下载量，接口口径要按下载量
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<ModelRepoFile>> ListFilesAsync(string repository,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get,
            $"{Endpoint}/api/v1/models/{repository}/repo/files?Revision={Revision}&Recursive=true");
        FilesResponse? response = await SendAsync<FilesResponse>(request, cancellationToken).ConfigureAwait(false);
        return response?.Data?.Files?
            .Where(x => x.Type == "blob" && !string.IsNullOrEmpty(x.Path))
            .Select(x => new ModelRepoFile(x.Path!, x.Size, string.IsNullOrEmpty(x.Sha256) ? null : x.Sha256))
            .ToList() ?? [];
    }

    public Task<string?> GetReadmeAsync(string repository, CancellationToken cancellationToken) =>
        ModelReadme.FetchAsync(httpClient, $"{Endpoint}/models/{repository}/resolve/{Revision}/README.md", AuthHeaders(),
            cancellationToken);

    public DownloadRequest CreateDownload(string repository, ModelRepoFile file, string destinationPath)
    {
        string path = string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString));
        return new DownloadRequest(new Uri($"{Endpoint}/models/{repository}/resolve/{Revision}/{path}"),
            destinationPath, 1, file.Sha256, AuthHeaders());
    }

    private Dictionary<string, string>? AuthHeaders() =>
        string.IsNullOrWhiteSpace(token) ? null : new() { ["Authorization"] = $"Bearer {token}" };

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        HttpRequestMessage request = new(method, url);
        foreach ((string key, string value) in AuthHeaders() ?? [])
            request.Headers.TryAddWithoutValidation(key, value);
        return request;
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken token)
    {
        using HttpResponseMessage response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: token).ConfigureAwait(false);
    }

    private sealed record SearchRequest(
        int PageSize,
        int PageNumber,
        string SortBy,
        string Target,
        object[] SingleCriterion,
        string Name);

    private sealed class SearchResponse
    {
        public SearchData? Data { get; set; }
    }

    private sealed class SearchData
    {
        public SearchModels? Model { get; set; }
    }

    private sealed class SearchModels
    {
        public List<SearchItem>? Models { get; set; }
    }

    // Path 是作者，Name 是仓库名
    private sealed class SearchItem
    {
        public string? Path { get; set; }
        public string? Name { get; set; }
        public long Downloads { get; set; }
    }

    private sealed class FilesResponse
    {
        public FilesData? Data { get; set; }
    }

    private sealed class FilesData
    {
        public List<FileItem>? Files { get; set; }
    }

    private sealed class FileItem
    {
        public string? Path { get; set; }
        public string? Type { get; set; }
        public long Size { get; set; }
        public string? Sha256 { get; set; }
    }
}
