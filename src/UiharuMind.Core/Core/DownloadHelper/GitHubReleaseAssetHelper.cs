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
    long Size);

public sealed record GitHubReleaseAssetSelectOptions(
    string? NamePrefix = null,
    string? NameSuffix = null,
    bool MatchCurrentPlatform = true,
    bool MatchCurrentArchitecture = true);

public static class GitHubReleaseAssetHelper
{
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
        if (dto == null || string.IsNullOrWhiteSpace(dto.TagName)) return null;

        List<GitHubReleaseAssetInfo> assets = dto.Assets?
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name) &&
                            !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            .Select(asset => new GitHubReleaseAssetInfo(
                asset.Name!,
                asset.BrowserDownloadUrl!,
                asset.Size))
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
        string assetsUrl =
            $"https://github.com/{owner}/{repository}/releases/expanded_assets/{Uri.EscapeDataString(decodedTagName)}";
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

        return new GitHubReleaseInfo(
            owner,
            repository,
            decodedTagName,
            document.QuerySelector(".Box-body h1")?.TextContent.Trim() ?? decodedTagName,
            document.Location.Href,
            ParseDateTime(document.QuerySelector(".Box-body relative-time")?.GetAttribute("datetime")),
            document.QuerySelector(".Box-body pre")?.TextContent,
            assets);
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
            return lowerName.Contains("linux");
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

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
