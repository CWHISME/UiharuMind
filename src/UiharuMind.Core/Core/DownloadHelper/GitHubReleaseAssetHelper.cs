using System.Xml.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AngleSharp;
using AngleSharp.Dom;

namespace UiharuMind.Core.Core.Utils;

public sealed record GitHubReleaseInfo(
    string Owner,
    string Repository,
    string TagName,
    string? Name,
    string ReleaseUrl,
    DateTimeOffset? PublishedAt,
    string? Body,
    IReadOnlyList<GitHubReleaseAssetInfo> Assets);

public sealed record GitHubReleaseAssetInfo(
    string Name,
    string DownloadUrl,
    long Size,
    string? Sha256 = null);

public sealed record GitHubReleaseAssetSelectOptions(
    string? NamePrefix = null,
    string? NameSuffix = null,
    bool MatchCurrentPlatform = true,
    bool MatchCurrentArchitecture = true);

public static class GitHubReleaseAssetHelper
{
    private const int RecentReleaseCount = 20;
    private const int FeedTagLimit = 5; // 网页兜底逐个请求，少看几个
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<GitHubReleaseInfo?> GetLatestReleaseAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetLatestReleaseFromApiAsync(owner, repository, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await GetLatestReleaseFromExpandedAssetsAsync(owner, repository, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 找最近一个满足条件的发布（含预发布）。llama.cpp 的构建都是预发布，正式版反而可能不带包，
    /// 只看 releases/latest 会落到空包上
    /// </summary>
    /// <param name="owner">仓库所有者</param>
    /// <param name="repository">仓库名</param>
    /// <param name="accept">发布是否可用（通常是「有本平台的包」）</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>发布；找不到为 null</returns>
    public static async Task<GitHubReleaseInfo?> FindLatestReleaseAsync(
        string owner,
        string repository,
        Func<GitHubReleaseInfo, bool> accept,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"https://api.github.com/repos/{owner}/{repository}/releases?per_page={RecentReleaseCount}";
            using HttpResponseMessage response = await HttpClient.GetAsync(url, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            List<GitHubReleaseDto>? dtos = await JsonSerializer.DeserializeAsync<List<GitHubReleaseDto>>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return dtos?.Select(dto => ToReleaseInfo(owner, repository, dto)).OfType<GitHubReleaseInfo>()
                .FirstOrDefault(accept);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // API 有匿名限流（每小时 60 次），退到网页：releases.atom 列出最近的标签，逐个看附件
            foreach (string tag in await GetRecentTagsFromFeedAsync(owner, repository, cancellationToken)
                         .ConfigureAwait(false))
            {
                GitHubReleaseInfo release = await GetReleaseFromExpandedAssetsAsync(
                    owner, repository, tag, $"https://github.com/{owner}/{repository}/releases/tag/{tag}",
                    cancellationToken).ConfigureAwait(false);
                if (accept(release)) return release;
            }

            return null;
        }
    }

    public static GitHubReleaseAssetInfo? SelectPlatformAsset(
        IEnumerable<GitHubReleaseAssetInfo> assets,
        GitHubReleaseAssetSelectOptions? options = null)
    {
        return SelectPlatformAssets(assets, options).FirstOrDefault();
    }

    public static IReadOnlyList<GitHubReleaseAssetInfo> SelectPlatformAssets(
        IEnumerable<GitHubReleaseAssetInfo> assets,
        GitHubReleaseAssetSelectOptions? options = null)
    {
        options ??= new GitHubReleaseAssetSelectOptions();
        IEnumerable<GitHubReleaseAssetInfo> query = assets;

        if (!string.IsNullOrWhiteSpace(options.NamePrefix))
        {
            query = query.Where(asset => asset.Name.StartsWith(
                options.NamePrefix, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(options.NameSuffix))
        {
            query = query.Where(asset => asset.Name.EndsWith(
                options.NameSuffix, StringComparison.OrdinalIgnoreCase));
        }

        List<GitHubReleaseAssetInfo> filtered = query.ToList();
        if (!options.MatchCurrentPlatform) return filtered;

        List<GitHubReleaseAssetInfo> platformMatched = filtered
            .Where(asset => MatchesCurrentPlatform(asset.Name))
            .ToList();
        if (platformMatched.Count == 0) return filtered;

        if (!options.MatchCurrentArchitecture) return platformMatched;

        List<GitHubReleaseAssetInfo> architectureMatched = platformMatched
            .Where(asset => MatchesCurrentArchitecture(asset.Name))
            .ToList();
        return architectureMatched.Count > 0 ? architectureMatched : platformMatched;
    }

    private static async Task<GitHubReleaseInfo?> GetLatestReleaseFromApiAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken)
    {
        string url = $"https://api.github.com/repos/{owner}/{repository}/releases/latest";
        using HttpResponseMessage response = await HttpClient.GetAsync(url, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(
            stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return dto == null ? null : ToReleaseInfo(owner, repository, dto);
    }

    private static GitHubReleaseInfo? ToReleaseInfo(string owner, string repository, GitHubReleaseDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TagName)) return null;

        List<GitHubReleaseAssetInfo> assets = dto.Assets?
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name) &&
                            !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            .Select(asset => new GitHubReleaseAssetInfo(
                asset.Name!,
                asset.BrowserDownloadUrl!,
                asset.Size,
                ParseSha256Digest(asset.Digest)))
            .ToList() ?? [];

        return new GitHubReleaseInfo(
            owner,
            repository,
            dto.TagName,
            dto.Name,
            dto.HtmlUrl ?? $"https://github.com/{owner}/{repository}/releases/tag/{dto.TagName}",
            dto.PublishedAt,
            dto.Body,
            assets);
    }

    private static async Task<GitHubReleaseInfo?> GetLatestReleaseFromExpandedAssetsAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken)
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);
        string latestUrl = $"https://github.com/{owner}/{repository}/releases/latest";
        IDocument document = await context.OpenAsync(latestUrl, cancellationToken)
            .ConfigureAwait(false);
        string tagName = document.Location.PathName.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(tagName) ||
            string.Equals(tagName, "latest", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string decodedTagName = Uri.UnescapeDataString(tagName);
        GitHubReleaseInfo release = await GetReleaseFromExpandedAssetsAsync(owner, repository, decodedTagName,
            document.Location.Href, cancellationToken).ConfigureAwait(false);
        return release with
        {
            Name = document.QuerySelector(".Box-body h1")?.TextContent.Trim() ?? decodedTagName,
            PublishedAt = ParseDateTime(document.QuerySelector(".Box-body relative-time")?.GetAttribute("datetime")),
            Body = document.QuerySelector(".Box-body pre")?.TextContent
        };
    }

    private static async Task<GitHubReleaseInfo> GetReleaseFromExpandedAssetsAsync(
        string owner,
        string repository,
        string tagName,
        string releaseUrl,
        CancellationToken cancellationToken)
    {
        var context = BrowsingContext.New(Configuration.Default.WithDefaultLoader());
        string assetsUrl =
            $"https://github.com/{owner}/{repository}/releases/expanded_assets/{Uri.EscapeDataString(tagName)}";
        IDocument assetsDocument = await context.OpenAsync(assetsUrl, cancellationToken)
            .ConfigureAwait(false);

        List<GitHubReleaseAssetInfo> assets = [];
        foreach (IElement link in assetsDocument.QuerySelectorAll("li a[href*='/releases/download/']"))
        {
            string? href = link.GetAttribute("href");
            string name = link.TextContent.Trim();
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            string downloadUrl = href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? href
                : "https://github.com" + href;
            assets.Add(new GitHubReleaseAssetInfo(name, downloadUrl, 0));
        }

        return new GitHubReleaseInfo(owner, repository, tagName, tagName, releaseUrl, null, null, assets);
    }

    private static async Task<IReadOnlyList<string>> GetRecentTagsFromFeedAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken)
    {
        string feed = await HttpClient.GetStringAsync($"https://github.com/{owner}/{repository}/releases.atom",
            cancellationToken).ConfigureAwait(false);
        return XDocument.Parse(feed).Descendants(XName.Get("link", "http://www.w3.org/2005/Atom"))
            .Select(x => x.Attribute("href")?.Value ?? "")
            .Where(x => x.Contains("/releases/tag/", StringComparison.Ordinal))
            .Select(x => Uri.UnescapeDataString(x[(x.LastIndexOf('/') + 1)..]))
            .Take(FeedTagLimit)
            .ToList();
    }

    private static bool MatchesCurrentPlatform(string name)
    {
        string lowerName = name.ToLowerInvariant();
        if (OperatingSystem.IsWindows())
        {
            return lowerName.Contains("win") || lowerName.Contains("windows");
        }

        if (OperatingSystem.IsMacOS())
        {
            return lowerName.Contains("mac") || lowerName.Contains("macos") ||
                   lowerName.Contains("osx") || lowerName.Contains("darwin");
        }

        if (OperatingSystem.IsLinux())
        {
            // llama.cpp 的 Linux 包以 ubuntu 命名
            return lowerName.Contains("linux") || lowerName.Contains("ubuntu");
        }

        return false;
    }

    private static bool MatchesCurrentArchitecture(string name)
    {
        string lowerName = name.ToLowerInvariant();
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => lowerName.Contains("arm64") || lowerName.Contains("aarch64"),
            Architecture.X64 => lowerName.Contains("x64") || lowerName.Contains("amd64"),
            Architecture.X86 => lowerName.Contains("x86") || lowerName.Contains("win32"),
            _ => false
        };
    }

    private static DateTimeOffset? ParseDateTime(string? raw)
    {
        return DateTimeOffset.TryParse(raw, out DateTimeOffset parsed) ? parsed : null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UiharuMind");
        return client;
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAssetDto>? Assets { get; set; }
    }

    // 附件的 digest 形如 "sha256:<hex>"，老发布没有
    private static string? ParseSha256Digest(string? digest)
    {
        const string prefix = "sha256:";
        return digest != null && digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? digest[prefix.Length..]
            : null;
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }
}
