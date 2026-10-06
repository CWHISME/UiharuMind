using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// <see cref="LLamaCppEngineInstaller.EnsureQueuedAsync"/> 的结果
/// </summary>
public enum EEngineEnsureState
{
    /// <summary>本机已有可用引擎</summary>
    AlreadyInstalled,

    /// <summary>引擎包已在队列里</summary>
    AlreadyQueued,

    /// <summary>这次排进了队列</summary>
    Queued,

    /// <summary>没找到本机能用的推荐包</summary>
    NoPackage
}

/// <summary>
/// 引擎包的「下载 → 解压校验 → 选中」：设置页与获取模型共用，都走全局下载队列
/// </summary>
/// <param name="queue">下载队列</param>
/// <param name="engineRoot">引擎根目录，判断队列里哪些是引擎包</param>
/// <param name="localVersions">本机已装的版本</param>
/// <param name="pullVersions">拉取远端版本（含本机已有的）</param>
/// <param name="install">解压校验一个已下好的包</param>
/// <param name="select">选中一个版本</param>
public sealed class LLamaCppEngineInstaller(
    DownloadQueue queue,
    string engineRoot,
    Func<Task<IReadOnlyList<VersionInfo>>> localVersions,
    Func<Task<IReadOnlyList<VersionInfo>>> pullVersions,
    Func<VersionInfo, CancellationToken, Task> install,
    Action<VersionInfo> select)
{
    private readonly SemaphoreSlim _ensureGate = new(1, 1); //连点两个量化时只排一次
    private VersionInfo? _availableUpdate;

    /// <summary>
    /// 走全局队列与 <see cref="LlmManager"/>
    /// </summary>
    public static LLamaCppEngineInstaller Shared { get; } = new(
        DownloadQueue.Shared,
        AppPaths.External.Engine,
        async () => (await LlmManager.Instance.GetLocalRuntimeVersions().ConfigureAwait(false)).VersionsList,
        async () => (await LlmManager.Instance.PullLatestRuntimeVersion().ConfigureAwait(false)).VersionsList,
        LlmManager.Instance.InstallRuntimeVersionAsync,
        LlmManager.Instance.SetSelectedRuntimeVersion);

    /// <summary>
    /// 一个引擎包装好了（在下载线程上触发）
    /// </summary>
    public event Action<VersionInfo>? Installed;

    /// <summary>
    /// 有没有新版本变了（可能在后台线程触发）
    /// </summary>
    public event Action? AvailableUpdateChanged;

    /// <summary>
    /// 比本机已装的都新的推荐包；没有为 null
    /// </summary>
    public VersionInfo? AvailableUpdate => _availableUpdate;

    /// <summary>
    /// 拉一次远端版本，看有没有比本机新的推荐包。拉不到不抛，只记日志
    /// </summary>
    /// <returns>新版本；没有为 null</returns>
    public async Task<VersionInfo?> CheckForUpdateAsync()
    {
        try
        {
            ApplyVersions(await pullVersions().ConfigureAwait(false));
        }
        catch (Exception e)
        {
            Log.Warning($"Check llama.cpp update failed: {e.Message}");
        }

        return _availableUpdate;
    }

    /// <summary>
    /// 按一份版本列表（含本机已装的）更新「有没有新版本」，手动检查更新拉到的列表也走这里
    /// </summary>
    /// <param name="versions">版本列表</param>
    public void ApplyVersions(IEnumerable<VersionInfo> versions) => SetAvailableUpdate(PickUpdate(versions));

    /// <summary>
    /// 挑出比已装的都新的推荐包。一个都没装时不算更新（那是 <see cref="EnsureQueuedAsync"/> 的事）
    /// </summary>
    /// <param name="versions">版本列表</param>
    /// <returns>新版本；没有为 null</returns>
    public static VersionInfo? PickUpdate(IEnumerable<VersionInfo> versions)
    {
        List<VersionInfo> list = versions.ToList();
        Version? installed = list.Where(x => x.IsInstalled).Select(x => x.Version).DefaultIfEmpty().Max();
        if (installed == null) return null;
        return list
            .Where(x => x.IsRecommended && !x.IsInstalled && !string.IsNullOrEmpty(x.DownloadUrl) && x.Version > installed)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();
    }

    /// <summary>
    /// 本机没有引擎、队列里也没有引擎包时，把推荐变体的最新包排进队列，装好后选中
    /// </summary>
    /// <param name="token">取消</param>
    /// <returns>结果，以及这次排进去的版本与下载项</returns>
    public async Task<(EEngineEnsureState State, VersionInfo? Version, DownloadJob? Job)> EnsureQueuedAsync(
        CancellationToken token = default)
    {
        await _ensureGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if ((await localVersions().ConfigureAwait(false)).Any(x => x.IsInstalled))
                return (EEngineEnsureState.AlreadyInstalled, null, null);
            if (queue.Jobs.Any(IsActiveEngineJob)) return (EEngineEnsureState.AlreadyQueued, null, null);

            VersionInfo? version = (await pullVersions().ConfigureAwait(false))
                .Where(x => x.IsRecommended && !x.IsInstalled && !string.IsNullOrEmpty(x.DownloadUrl))
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();
            if (version == null) return (EEngineEnsureState.NoPackage, null, null);

            return (EEngineEnsureState.Queued, version, Enqueue(version, true));
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    /// <summary>
    /// 把一个引擎包排进队列，下完就地安装
    /// </summary>
    /// <param name="version">引擎版本</param>
    /// <param name="selectWhenInstalled">装好后选中它</param>
    /// <returns>下载项</returns>
    public DownloadJob Enqueue(VersionInfo version, bool selectWhenInstalled)
    {
        DownloadRequest request = new(new Uri(ModelSources.ApplyGitHubProxy(version.DownloadUrl)),
            version.PackageFilePath, version.SegmentCount, version.Sha256);
        return queue.Enqueue(version.Name, request, async token =>
        {
            await InstallAsync(version, token).ConfigureAwait(false);
            if (selectWhenInstalled) select(version);
        });
    }

    /// <summary>
    /// 安装一个已下好的引擎包
    /// </summary>
    /// <param name="version">引擎版本</param>
    /// <param name="token">取消</param>
    public async Task InstallAsync(VersionInfo version, CancellationToken token = default)
    {
        await install(version, token).ConfigureAwait(false);
        if (_availableUpdate != null && version.Version >= _availableUpdate.Version) SetAvailableUpdate(null);
        Installed?.Invoke(version);
    }

    private void SetAvailableUpdate(VersionInfo? update)
    {
        if (_availableUpdate?.Name == update?.Name) return;
        _availableUpdate = update;
        AvailableUpdateChanged?.Invoke();
    }

    private bool IsActiveEngineJob(DownloadJob job)
    {
        if (job.State is EDownloadJobState.Completed or EDownloadJobState.Failed) return false;
        string root = Path.GetFullPath(engineRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(job.Request.DestinationPath).StartsWith(root, StringComparison.Ordinal);
    }
}
