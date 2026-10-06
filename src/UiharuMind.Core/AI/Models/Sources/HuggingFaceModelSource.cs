using System.Text.Json;
using System.Text.Json.Serialization;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.AI.Models.Sources;

/// <summary>
/// HuggingFace 及与其接口兼容的镜像（hf-mirror 等），镜像只换地址不换协议
/// </summary>
/// <param name="httpClient">HTTP 客户端</param>
/// <param name="endpoint">站点根地址，如 https://huggingface.co</param>
/// <param name="token">访问令牌（受限仓库要），镜像也照发</param>
public sealed class HuggingFaceModelSource(HttpClient httpClient, string endpoint, string? token) : IModelSource
{
    public const string SourceId = "huggingface";
    private const string Revision = "main";

    private readonly string _endpoint = endpoint.TrimEnd('/');

    public string Id => SourceId;

    public async Task<IReadOnlyList<ModelRepoSummary>> SearchAsync(string query, int limit,
        CancellationToken cancellationToken)
    {
        string url = $"{_endpoint}/api/models?search={Uri.EscapeDataString(query)}&filter=gguf&sort=downloads&limit={limit}";
        List<SearchItem>? items = await GetJsonAsync<List<SearchItem>>(url, cancellationToken).ConfigureAwait(false);
        return items?.Where(x => !string.IsNullOrEmpty(x.Id)).Select(x => new ModelRepoSummary(x.Id!, x.Downloads))
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<ModelRepoFile>> ListFilesAsync(string repository,
        CancellationToken cancellationToken)
    {
        string url = $"{_endpoint}/api/models/{repository}/tree/{Revision}?recursive=true";
        List<TreeItem>? items = await GetJsonAsync<List<TreeItem>>(url, cancellationToken).ConfigureAwait(false);
        return items?.Where(x => x.Type == "file" && !string.IsNullOrEmpty(x.Path))
            .Select(x => new ModelRepoFile(x.Path!, x.Lfs?.Size ?? x.Size, x.Lfs?.Oid))
            .ToList() ?? [];
    }

    public DownloadRequest CreateDownload(string repository, ModelRepoFile file, string destinationPath)
    {
        string path = string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString));
        return new DownloadRequest(new Uri($"{_endpoint}/{repository}/resolve/{Revision}/{path}"), destinationPath,
            1, file.Sha256, AuthHeaders());
    }

    private Dictionary<string, string>? AuthHeaders() =>
        string.IsNullOrWhiteSpace(token) ? null : new() { ["Authorization"] = $"Bearer {token}" };

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        foreach ((string key, string value) in AuthHeaders() ?? [])
            request.Headers.TryAddWithoutValidation(key, value);
        using HttpResponseMessage response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: token).ConfigureAwait(false);
    }

    private sealed class SearchItem
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("downloads")] public long Downloads { get; set; }
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
}
