using System.Collections.Concurrent;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Utils;

public abstract class ReleaseVersionManagerBase<TVersion> where TVersion : ManagedVersionPackage, new()
{
    /// <summary>
    /// 工作列表，只在 <see cref="ExclusiveAsync"/> 里改；对外给的是 <see cref="VersionsList"/> 快照
    /// </summary>
    protected readonly List<TVersion> Versions = [];

    private readonly SemaphoreSlim _sync = new(1, 1); //扫描与拉取互斥：设置页、启动检查更新、装引擎会同时来
    private TVersion[] _snapshot = [];

    /// <summary>
    /// 同一个安装目录同时只能有一个线程在装（下载队列自动装、设置页手动装会并发进来）；
    /// 仅保证进程内串行——多实例共享档案时进程间仍可能同时装，但唯一临时目录已避免互相踩踏。
    /// 不同版本/引擎各用各的锁互不阻塞。版本数有限，锁不回收。
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InstallGates = new();

    // 杀软实时扫描刚解压的 DLL/EXE 时可能锁几秒，目录删除与移动都要扛得住这个窗口
    private const int DirectoryRetryAttempts = 5;

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

    /// <summary>
    /// 解压校验一个已下好的包，成功换上正式目录
    /// </summary>
    /// <param name="version">要装的版本</param>
    /// <param name="deleteArchive">装好后是否删压缩包</param>
    /// <param name="cancellationToken">取消</param>
    public async Task InstallArchiveAsync(
        TVersion version,
        bool deleteArchive = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(version.InstallDirectory))
            throw new ArgumentException("Install directory is not set.", nameof(version));

        // key 归一（末尾分隔符去掉、大小写由 Windows 文件系统保证一致）；不解析符号链接，约定同形路径
        string installDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(version.InstallDirectory));
        SemaphoreSlim gate = InstallGates.GetOrAdd(installDirectory, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 并发的另一份安装可能已经装好并删掉了压缩包（deleteArchive）：
            // 目标目录已有效就直接算完成，别让第二份报" Archive file does not exist "
            if (!File.Exists(version.PackageFilePath))
            {
                ValidateAndRefresh(version);
                if (version.IsInstalled)
                {
                    Log.Debug($"Package archive already gone, install directory is valid: {version.InstallDirectory}");
                    await OnArchiveInstalledAsync(version, cancellationToken).ConfigureAwait(false);
                    return;
                }

                throw new FileNotFoundException("Archive file does not exist.", version.PackageFilePath);
            }

            TryCleanLegacyInstallingDirectory(installDirectory);

            // 临时目录名带唯一后缀：并发的两次安装各装各的，不再共享同一个 ".installing" 互相踩
            string tempDirectory = installDirectory + ".installing." + Guid.NewGuid().ToString("N")[..8];
            string? backupDirectory = null;
            bool movedIn = false; //只有自己移入正式位的新内容才允许在回滚时被删
            bool installValidated = false; //新内容确认有效后，后续异常一律不再回滚
            try
            {
                await SimpleArchiveHelper
                    .ExtractArchiveAsync(version.PackageFilePath, tempDirectory, false, cancellationToken)
                    .ConfigureAwait(false);

                // 旧目录先改名靠边再移入新的：移动失败还能回滚，不会出现新旧皆无
                if (Directory.Exists(installDirectory))
                {
                    backupDirectory = installDirectory + ".old." + Guid.NewGuid().ToString("N")[..8];
                    await MoveDirectoryWithRetryAsync(installDirectory, backupDirectory, cancellationToken)
                        .ConfigureAwait(false);
                }

                await MoveDirectoryWithRetryAsync(tempDirectory, installDirectory, cancellationToken)
                    .ConfigureAwait(false);
                movedIn = true; //新内容已进正式位；回滚时只删它（旧内容要么在 backup、要么还没动）

                ValidateAndRefresh(version);
                if (!version.IsInstalled)
                {
                    throw new InvalidDataException(version.ValidationError ?? "Installed package is invalid.");
                }

                installValidated = true; //新内容已生效，下面再出异常只能报失败，不能把已装好的删掉

                // 校验通过才删旧备份：校验失败时它还是回滚的底牌，不能提前删
                if (backupDirectory != null)
                {
                    try
                    {
                        await DeleteDirectoryWithRetryAsync(backupDirectory, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Failed to clean old install backup: {backupDirectory}, {e.Message}");
                    }
                }

                // 装好了但包删不掉（被锁/杀软）不算安装失败：包留着下次还能用
                if (deleteArchive && File.Exists(version.PackageFilePath))
                {
                    try
                    {
                        File.Delete(version.PackageFilePath);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Failed to delete archive after install: {version.PackageFilePath}, {e.Message}");
                    }
                }

                version.RefreshState();
                await OnArchiveInstalledAsync(version, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 移入失败或校验失败：把靠边的旧目录滚回来，别让该版本名空着/变成无效内容。
                // 校验已通过后不再回滚（新版本是好的）；回滚最该重试（失败典型原因是文件被锁/杀软拒操作），
                // 且不能被取消打断。
                if (backupDirectory != null && !installValidated)
                {
                    if (movedIn && Directory.Exists(installDirectory))
                    {
                        // 正式位里是刚移入的未通过校验内容，先腾出位置再回滚；
                        // （move-old 失败时正式位还是旧的有效版本，movedIn 为 false，绝不删）
                        try
                        {
                            await DeleteDirectoryWithRetryAsync(installDirectory, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            Log.Error(
                                $"Failed to remove invalid install directory before rollback: {installDirectory}, {e.Message}");
                        }
                    }

                    if (Directory.Exists(backupDirectory) && !Directory.Exists(installDirectory))
                    {
                        try
                        {
                            await MoveDirectoryWithRetryAsync(backupDirectory, installDirectory, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            Log.Error(
                                $"Failed to roll back old install directory: {backupDirectory} -> {installDirectory}, {e.Message}");
                        }
                    }
                }

                throw;
            }
            finally
            {
                // 清理失败只记日志不抛出：别让"临时目录残留"掩盖安装本身的结果；
                // 最坏多等约 3 秒（4 次退避 300/600/900/1200ms）且期间仍持有本目录的安装锁
                try
                {
                    await DeleteDirectoryWithRetryAsync(tempDirectory, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Warning($"Failed to clean temp install directory: {tempDirectory}, {e.Message}");
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    // 旧版本留下的固定名 .installing 残留：删得掉就删，删不掉不碍事（新临时目录带唯一后缀）
    private void TryCleanLegacyInstallingDirectory(string installDirectory)
    {
        string legacy = installDirectory + ".installing";
        try
        {
            if (Directory.Exists(legacy)) Directory.Delete(legacy, true);
        }
        catch (Exception e)
        {
            Log.Debug($"Old .installing directory left behind: {legacy}, {e.Message}");
        }
    }

    private static async Task DeleteDirectoryWithRetryAsync(string directory, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                return;
            }
            catch (Exception e) when (IsRetryable(e, attempt))
            {
                await DelayForRetry(attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task MoveDirectoryWithRetryAsync(string source, string destination,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception e) when (IsRetryable(e, attempt))
            {
                await DelayForRetry(attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // 并发删除/移动撞的是 IOException（被占用）或 UnauthorizedAccessException（杀软拒删）
    private static bool IsRetryable(Exception e, int attempt)
    {
        return attempt < DirectoryRetryAttempts &&
               e is IOException or UnauthorizedAccessException;
    }

    private static async Task DelayForRetry(int attempt, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken).ConfigureAwait(false);
    }

    // 安装过程中的非版本目录：临时解压目录（.installing / .installing.*）与旧版备份（.old.*）。
    // 版本名全部来自 GitHub 发布 tag（llama.cpp / whisper.cpp / sd.cpp / audio-* / UiharuMind-*），不会带这些子串
    private static bool IsTransientInstallDirectory(string directory)
    {
        string name = Path.GetFileName(directory);
        return name.Contains(".installing", StringComparison.Ordinal)
            || name.Contains(".old.", StringComparison.Ordinal)
            || name.EndsWith(".old", StringComparison.Ordinal);
    }

    // 固定名临时目录：新代码只在 .installing.<guid> / .old.<guid> 下工作，固定名 .installing 撞见必是历史残留
    private static bool IsLegacyFixedInstallingDirectory(string directory) =>
        Path.GetFileName(directory).EndsWith(".installing", StringComparison.Ordinal);

    private static void TryDeleteTransientInstallDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch (Exception e)
        {
            Log.Debug($"Transient install directory left behind: {directory}, {e.Message}");
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
                // 安装过程中的临时/备份目录（.installing / .installing.* / .old.*）不是版本，必须跳过。
                // 唯一名目录可能正被另一个线程安装中，绝不删除；固定名 .installing 新代码已不再创建，
                // 撞见必然是历史残留，可以顺手删。
                if (IsTransientInstallDirectory(versionDirectory))
                {
                    if (IsLegacyFixedInstallingDirectory(versionDirectory))
                    {
                        TryDeleteTransientInstallDirectory(versionDirectory);
                    }

                    continue;
                }

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
