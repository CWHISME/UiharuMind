namespace UiharuMind.Core.Core.Utils;

public abstract class ReleaseVersionManagerBase<TVersion> where TVersion : ManagedVersionPackage, new()
{
    /// <summary>
    /// 工作列表，只在 <see cref="ExclusiveAsync"/> 里改；对外给的是 <see cref="VersionsList"/> 快照
    /// </summary>
    protected readonly List<TVersion> Versions = [];

    private readonly SemaphoreSlim _sync = new(1, 1); //扫描与拉取互斥：设置页、启动检查更新、装引擎会同时来
    private TVersion[] _snapshot = [];

    protected abstract string Owner { get; }
    protected abstract string Repository { get; }

    /// <summary>
    /// 是否在预发布里找：为 true 时取最近一个带本平台包的发布，而不是 releases/latest
    /// </summary>
    protected virtual bool IncludePrereleases => false;

    public string? ReleaseInfoText { get; protected set; }
    public GitHubReleaseInfo? LatestReleaseInfo { get; protected set; }

    /// <summary>
    /// 最近一次扫描或拉取的结果（快照，调用方随便遍历）
    /// </summary>
    public IReadOnlyList<TVersion> VersionsList => Volatile.Read(ref _snapshot);

    public Task<IReadOnlyList<TVersion>> GetLocalVersionsAsync(
        string rootDirectory,
        bool forceNew = false,
        CancellationToken cancellationToken = default)
    {
        return ExclusiveAsync(() => ScanLocalVersionsAsync(rootDirectory, Versions, forceNew || Versions.Count > 0,
            null, cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyList<TVersion>> PullLatestVersionsAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        return ExclusiveAsync(async () =>
        {
            await ScanLocalVersionsAsync(rootDirectory, Versions, true, null, cancellationToken).ConfigureAwait(false);

            IReadOnlyList<GitHubReleaseInfo> releases = await ResolveRemoteReleasesAsync(HasUsableAssets, cancellationToken)
                .ConfigureAwait(false);
            if (releases.Count == 0) return;

            LatestReleaseInfo = releases[0];
            ReleaseInfoText = CreateReleaseInfoText(releases[0]);
            foreach (GitHubReleaseInfo release in releases)
            {
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
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 独占地改工作列表，完了排序并发布快照
    /// </summary>
    /// <param name="body">改列表的活</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>新快照</returns>
    protected async Task<IReadOnlyList<TVersion>> ExclusiveAsync(Func<Task> body, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await body().ConfigureAwait(false);
            SortVersions();
            TVersion[] snapshot = Versions.ToArray();
            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
        finally
        {
            _sync.Release();
        }
    }

    private bool HasUsableAssets(GitHubReleaseInfo release)
    {
        return GitHubReleaseAssetHelper.SelectPlatformAssets(release.Assets, GetAssetSelectOptions())
            .Any(ShouldIncludeAsset);
    }

    /// <summary>
    /// 去哪几个发布拉包，第一个的说明作为发布说明；默认只拉 <see cref="ResolveRemoteReleaseAsync"/> 那一个
    /// </summary>
    /// <param name="accept">发布是否可用（通常是「有本平台的包」）</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>发布；一个都没有为空</returns>
    protected virtual async Task<IReadOnlyList<GitHubReleaseInfo>> ResolveRemoteReleasesAsync(
        Func<GitHubReleaseInfo, bool> accept,
        CancellationToken cancellationToken)
    {
        GitHubReleaseInfo? release = await ResolveRemoteReleaseAsync(accept, cancellationToken).ConfigureAwait(false);
        return release == null ? [] : [release];
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
