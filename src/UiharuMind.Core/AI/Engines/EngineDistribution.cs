using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.AI.Engines;

/// <summary>
/// 选中的引擎版本存在哪（各引擎自己的设置）
/// </summary>
public interface IEngineVersionSelection
{
    /// <summary>
    /// 选中的版本名
    /// </summary>
    string? SelectedRuntimeVersion { get; set; }

    /// <summary>
    /// 落盘
    /// </summary>
    void Save();
}

/// <summary>
/// 一种本地引擎的分发：本机装了哪些版本、在用哪个（选中的装着就用它，不然第一个装着的）、远端有什么、怎么下怎么装。
/// 各引擎只给版本管理器、目录与选中存在哪；进程与模型不归这里
/// </summary>
public sealed class EngineDistribution<TPackage> : IEngineDistribution where TPackage : EnginePackage, new()
{
    private readonly EngineVersionManager<TPackage> _versions;
    private readonly IEngineVersionSelection _selection;
    private readonly Func<string, string?> _releaseUrl;
    private readonly Lock _gate = new();
    private TPackage? _current;
    private bool _scanned; //扫过一次本机才知道在用哪个

    /// <param name="name">引擎名</param>
    /// <param name="rootDirectory">引擎包目录（各引擎的目录不能互相包含）</param>
    /// <param name="versions">版本管理器</param>
    /// <param name="queue">下载队列</param>
    /// <param name="selection">选中的版本存在哪</param>
    /// <param name="releaseUrl">按版本名拼发布页；认不出为 null</param>
    /// <param name="channels">更新通道；没有为 null</param>
    /// <param name="isVetted">哪些版本可以提示更新、自动装；null 全认</param>
    public EngineDistribution(string name, string rootDirectory, EngineVersionManager<TPackage> versions,
        DownloadQueue queue, IEngineVersionSelection selection, Func<string, string?> releaseUrl,
        IEngineUpdateChannels? channels = null, Func<TPackage, bool>? isVetted = null)
    {
        Name = name;
        RootDirectory = rootDirectory;
        Channels = channels;
        _versions = versions;
        _selection = selection;
        _releaseUrl = releaseUrl;
        Installer = new EngineInstaller<TPackage>(name, queue, rootDirectory,
            () => ScanAsync(), () => PullLatestVersionsAsync(), InstallArchiveAsync, Select, isVetted);
        Installer.Installed += _ => Installed?.Invoke();
        Installer.AvailableUpdateChanged += () => AvailableUpdateChanged?.Invoke();
    }

    public event Action? Installed;
    public event Action? AvailableUpdateChanged;
    public event Action? VersionChanged;

    public string Name { get; }
    public string RootDirectory { get; }
    public IEngineUpdateChannels? Channels { get; }

    /// <summary>
    /// 引擎包的下载与安装
    /// </summary>
    public EngineInstaller<TPackage> Installer { get; }

    /// <summary>
    /// 在用的版本；没装（或还没扫过）为 null
    /// </summary>
    public TPackage? CurrentVersion
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public EnginePackage? AvailableUpdate => Installer.AvailableUpdate;

    /// <summary>
    /// 最近一次拉到的发布说明；没拉过为 null
    /// </summary>
    public string? ReleaseInfoText => _versions.ReleaseInfoText;

    /// <summary>
    /// 扫本机已装的版本，顺带确定在用的那个
    /// </summary>
    /// <param name="cancellationToken">取消</param>
    /// <returns>版本快照</returns>
    public async Task<IReadOnlyList<TPackage>> ScanAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootDirectory);
        IReadOnlyList<TPackage> versions = await _versions.ScanLocalAsync(RootDirectory, cancellationToken)
            .ConfigureAwait(false);
        string? selected = _selection.SelectedRuntimeVersion;
        Use(versions.FirstOrDefault(x => x.IsInstalled && x.Name == selected)
            ?? versions.FirstOrDefault(x => x.IsInstalled));
        _scanned = true;
        return versions;
    }

    /// <summary>
    /// 在用的版本；还没扫过就先扫一次
    /// </summary>
    /// <param name="cancellationToken">取消</param>
    /// <returns>版本；没装为 null</returns>
    public async Task<TPackage?> EnsureCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (!_scanned) await ScanAsync(cancellationToken).ConfigureAwait(false);
        return CurrentVersion;
    }

    /// <summary>
    /// 拉远端版本（含本机已有的），顺带刷新「有没有新版本」
    /// </summary>
    /// <param name="cancellationToken">取消</param>
    /// <returns>版本快照</returns>
    public async Task<IReadOnlyList<TPackage>> PullLatestVersionsAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootDirectory);
        IReadOnlyList<TPackage> versions = await _versions.PullLatestVersionsAsync(RootDirectory, cancellationToken)
            .ConfigureAwait(false);
        Installer.ApplyVersions(versions);
        return versions;
    }

    /// <summary>
    /// 解压校验一个已下好的包，成功后删掉压缩包
    /// </summary>
    private Task InstallArchiveAsync(TPackage version, CancellationToken cancellationToken)
    {
        return _versions.InstallArchiveAsync(version, true, cancellationToken);
    }

    /// <summary>
    /// 选中一个版本并落盘
    /// </summary>
    /// <param name="version">版本；null 为不选</param>
    public void Select(TPackage? version)
    {
        Use(version);
        _selection.SelectedRuntimeVersion = version?.Name;
        _selection.Save();
    }

    /// <summary>
    /// 直接换用一个版本，不落盘（扫盘时与测试用）
    /// </summary>
    /// <param name="version">版本</param>
    internal void Use(TPackage? version)
    {
        bool changed;
        lock (_gate)
        {
            changed = _current?.Name != version?.Name;
            _current = version;
        }

        if (changed) VersionChanged?.Invoke();
    }

    EnginePackage? IEngineDistribution.CurrentVersion => CurrentVersion;

    async Task<IReadOnlyList<EnginePackage>> IEngineDistribution.GetLocalVersionsAsync() =>
        await ScanAsync().ConfigureAwait(false);

    async Task<EngineRemoteVersions> IEngineDistribution.PullRemoteVersionsAsync()
    {
        IReadOnlyList<TPackage> versions = await PullLatestVersionsAsync().ConfigureAwait(false);
        return new EngineRemoteVersions(versions, ReleaseInfoText);
    }

    void IEngineDistribution.Select(EnginePackage version) => Select((TPackage)version);

    Task IEngineDistribution.InstallAsync(EnginePackage version) => Installer.InstallAsync((TPackage)version);

    DownloadJob IEngineDistribution.Enqueue(EnginePackage version) => Installer.Enqueue((TPackage)version, true);

    /// <summary>
    /// 本机没有可用版本时把推荐包排进下载队列
    /// </summary>
    /// <param name="token">取消</param>
    /// <returns>结果、排进去的版本与下载项</returns>
    public async Task<(EEngineEnsureState State, EnginePackage? Version, DownloadJob? Job)> EnsureQueuedAsync(
        CancellationToken token = default)
    {
        (EEngineEnsureState state, TPackage? version, DownloadJob? job) =
            await Installer.EnsureQueuedAsync(token).ConfigureAwait(false);
        return (state, version, job);
    }

    Task IEngineDistribution.CheckForUpdateAsync() => Installer.CheckForUpdateAsync();

    string? IEngineDistribution.ReleaseUrl(EnginePackage version) => _releaseUrl(version.Name);
}
