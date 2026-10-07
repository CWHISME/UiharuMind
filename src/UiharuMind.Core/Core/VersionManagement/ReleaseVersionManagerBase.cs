namespace UiharuMind.Core.Core.Utils;

public abstract class ReleaseVersionManagerBase<TVersion> where TVersion : ManagedVersionPackage, new()
{
    protected readonly List<TVersion> Versions = [];

    protected abstract string Owner { get; }
    protected abstract string Repository { get; }

    /// <summary>
    /// 是否在预发布里找：为 true 时取最近一个带本平台包的发布，而不是 releases/latest
    /// </summary>
    protected virtual bool IncludePrereleases => false;

    public string? ReleaseInfoText { get; protected set; }
    public GitHubReleaseInfo? LatestReleaseInfo { get; protected set; }
    public IReadOnlyList<TVersion> VersionsList => Versions;

    public async Task<IReadOnlyList<TVersion>> GetLocalVersionsAsync(
        string rootDirectory,
        bool forceNew = false,
        CancellationToken cancellationToken = default)
    {
        await GetLocalVersionsAsync(rootDirectory, Versions, forceNew || Versions.Count > 0, cancellationToken)
            .ConfigureAwait(false);

        return Versions;
    }

    public async Task<IReadOnlyList<TVersion>> GetLocalVersionsAsync(
        string rootDirectory,
        List<TVersion> versions,
        bool clearVersions = false,
        CancellationToken cancellationToken = default)
    {
        await ScanLocalVersionsAsync(rootDirectory, versions, clearVersions, null, cancellationToken)
            .ConfigureAwait(false);
        return versions;
    }

    public async Task<IReadOnlyList<TVersion>> PullLatestVersionsAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        await GetLocalVersionsAsync(rootDirectory, true, cancellationToken).ConfigureAwait(false);

        GitHubReleaseInfo? release = await ResolveRemoteReleaseAsync(HasUsableAssets, cancellationToken)
            .ConfigureAwait(false);
        if (release == null) return Versions;

        LatestReleaseInfo = release;
        ReleaseInfoText = CreateReleaseInfoText(release);
        IReadOnlyList<GitHubReleaseAssetInfo> assets = GitHubReleaseAssetHelper.SelectPlatformAssets(
            release.Assets,
            GetAssetSelectOptions());

        foreach (GitHubReleaseAssetInfo asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ShouldIncludeAsset(asset)) continue;

            TVersion version = CreateVersionFromAssetCore(release, asset, rootDirectory);
            TVersion? existing = Versions.FirstOrDefault(x => x.Name == version.Name);
            if (existing is { IsInstalled: true }) continue;

            ValidateAndRefresh(version);
            AddOrReplaceVersion(version);
        }

        SortVersions();
        return Versions;
    }

    private bool HasUsableAssets(GitHubReleaseInfo release)
    {
        return GitHubReleaseAssetHelper.SelectPlatformAssets(release.Assets, GetAssetSelectOptions())
            .Any(ShouldIncludeAsset);
    }

    /// <summary>
    /// 决定去哪个发布拉包：默认预发布走最近可用、正式走 releases/latest
    /// </summary>
    /// <param name="accept">发布是否可用（通常是「有本平台的包」）</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>发布；找不到为 null</returns>
    protected virtual Task<GitHubReleaseInfo?> ResolveRemoteReleaseAsync(
        Func<GitHubReleaseInfo, bool> accept,
        CancellationToken cancellationToken)
    {
        return IncludePrereleases
            ? GitHubReleaseAssetHelper.FindLatestReleaseAsync(Owner, Repository, accept, cancellationToken)
            : GitHubReleaseAssetHelper.GetLatestReleaseAsync(Owner, Repository, cancellationToken);
    }

    public async Task InstallArchiveAsync(
        TVersion version,
        bool deleteArchive = true,
        CancellationToken cancellationToken = default)
    {
        string tempDirectory = version.InstallDirectory + ".installing";
        if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);

        try
        {
            await SimpleArchiveHelper
                .ExtractArchiveAsync(version.PackageFilePath, tempDirectory, false, cancellationToken)
                .ConfigureAwait(false);

            if (Directory.Exists(version.InstallDirectory)) Directory.Delete(version.InstallDirectory, true);
            Directory.Move(tempDirectory, version.InstallDirectory);
            ValidateAndRefresh(version);
            if (!version.IsInstalled)
            {
                throw new InvalidDataException(version.ValidationError ?? "Installed package is invalid.");
            }

            if (deleteArchive && File.Exists(version.PackageFilePath)) File.Delete(version.PackageFilePath);
            version.RefreshState();
            await OnArchiveInstalledAsync(version, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }
    }

    private static TVersion CreateVersion(string name)
    {
        return new TVersion
        {
            Name = name,
            Version = ManagedVersionPackage.ParseVersion(name)
        };
    }

    protected virtual void ConfigureLocalVersion(TVersion version, string versionDirectory)
    {
    }

    protected virtual GitHubReleaseAssetSelectOptions GetAssetSelectOptions()
    {
        return new GitHubReleaseAssetSelectOptions();
    }

    protected virtual bool ShouldIncludeAsset(GitHubReleaseAssetInfo asset)
    {
        return true;
    }

    protected async Task ScanLocalVersionsAsync(
        string rootDirectory,
        List<TVersion> versions,
        bool clearVersions = false,
        Action<TVersion>? configureVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (clearVersions)
        {
            versions.Clear();
        }

        Directory.CreateDirectory(rootDirectory);
        await Task.Run(() =>
        {
            foreach (string versionDirectory in Directory.EnumerateDirectories(rootDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TVersion version = CreateVersionFromDirectoryCore(versionDirectory);
                configureVersion?.Invoke(version);
                ValidateAndRefresh(version);
                if (ShouldKeepLocalVersion(version)) AddOrReplaceVersion(versions, version);
            }

            SortVersions(versions);
        }, cancellationToken).ConfigureAwait(false);
    }

    private TVersion CreateVersionFromDirectoryCore(string versionDirectory)
    {
        TVersion version = CreateVersion(Path.GetFileName(versionDirectory));
        version.InstallDirectory = versionDirectory;
        ConfigureLocalVersion(version, versionDirectory);
        return version;
    }

    private TVersion CreateVersionFromAssetCore(
        GitHubReleaseInfo release,
        GitHubReleaseAssetInfo asset,
        string rootDirectory)
    {
        string versionName = GetRemoteVersionName(release, asset);
        TVersion version = CreateVersion(versionName);
        version.Version = ManagedVersionPackage.ParseVersion(release.TagName);
        version.ReleaseUrl = release.ReleaseUrl;
        version.PublishedAt = release.PublishedAt;
        version.ReleaseBody = release.Body;
        version.DownloadUrl = asset.DownloadUrl;
        version.PackageFilePath = GetPackageFilePath(rootDirectory, asset.Name);
        version.InstallDirectory = GetRemoteInstallDirectory(rootDirectory, versionName, asset);
        version.AssetSize = asset.Size;
        version.Sha256 = asset.Sha256;
        return version;
    }

    protected virtual string GetRemoteVersionName(GitHubReleaseInfo release, GitHubReleaseAssetInfo asset)
    {
        return SimpleArchiveHelper.GetNameWithoutArchiveExtension(asset.Name);
    }

    protected virtual string GetRemoteInstallDirectory(
        string rootDirectory,
        string versionName,
        GitHubReleaseAssetInfo asset)
    {
        return GetInstallDirectoryFromVersion(rootDirectory, versionName);
    }

    protected virtual ManagedVersionValidationResult ValidateInstalledVersion(TVersion version)
    {
        return ReleaseVersionPackageManager.IsInstalledDirectory(version.InstallDirectory)
            ? ManagedVersionValidationResult.Valid
            : new ManagedVersionValidationResult(false, "Install directory is empty.");
    }

    protected virtual bool ShouldKeepLocalVersion(TVersion version)
    {
        return version.IsInstalled;
    }

    protected virtual Task OnArchiveInstalledAsync(TVersion version, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected virtual string CreateReleaseInfoText(GitHubReleaseInfo release)
    {
        string? releaseDate = release.PublishedAt?.ToString("O");
        return $"Release Date: {releaseDate} \n\n{release.Body}";
    }

    protected static string GetPackageFilePath(string rootDirectory, string packageName)
    {
        return ReleaseVersionPackageManager.GetPackageFilePath(rootDirectory, packageName);
    }

    protected static string GetInstallDirectoryFromPackageName(string rootDirectory, string packageName)
    {
        return ReleaseVersionPackageManager.GetInstallDirectoryFromPackageName(rootDirectory, packageName);
    }

    protected static string GetInstallDirectoryFromVersion(string rootDirectory, string versionName)
    {
        return ReleaseVersionPackageManager.GetInstallDirectoryFromVersion(rootDirectory, versionName);
    }

    protected void ValidateAndRefresh(TVersion version)
    {
        ManagedVersionValidationResult result = ValidateInstalledVersion(version);
        version.IsInstalled = result.IsValid;
        version.ValidationError = result.Error;
        version.RefreshState();
    }

    protected void AddOrReplaceVersion(TVersion version)
    {
        AddOrReplaceVersion(Versions, version);
    }

    protected void AddOrReplaceVersion(List<TVersion> versions, TVersion version)
    {
        int index = versions.FindIndex(x => x.Name == version.Name);
        if (index >= 0)
        {
            versions[index] = version;
            return;
        }

        versions.Add(version);
    }

    protected virtual void SortVersions()
    {
        SortVersions(Versions);
    }

    protected virtual void SortVersions(List<TVersion> versions)
    {
        versions.Sort((left, right) => left.CompareTo(right));
    }
}
