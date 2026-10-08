using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.AI.Engines;

/// <summary>
/// 拉一次远端版本的结果
/// </summary>
/// <param name="Versions">版本列表（含本机已有的）</param>
/// <param name="ReleaseInfo">最新版本说明（markdown）；拉不到为 null</param>
public sealed record EngineRemoteVersions(IReadOnlyList<EnginePackage> Versions, string? ReleaseInfo);

/// <summary>
/// 一种本地引擎的分发面：本机版本、远端版本、选中、安装、更新提示。
/// 引擎版本卡、获取模型时自动装引擎、启动时检查更新都只认这一层
/// </summary>
public interface ILocalEngine
{
    /// <summary>
    /// 引擎名，如 llama.cpp
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 引擎包所在目录
    /// </summary>
    string RootDirectory { get; }

    /// <summary>
    /// 在用的版本；没装为 null
    /// </summary>
    EnginePackage? CurrentVersion { get; }

    /// <summary>
    /// 比本机已装的都新的推荐包；没有为 null
    /// </summary>
    EnginePackage? AvailableUpdate { get; }

    /// <summary>
    /// 更新通道（预览 / 正式）；没有通道的引擎为 null
    /// </summary>
    IEngineUpdateChannels? Channels { get; }

    /// <summary>
    /// 装好了一个包（可能在下载线程上）
    /// </summary>
    event Action? Installed;

    /// <summary>
    /// 有没有新版本变了（可能在后台线程）
    /// </summary>
    event Action? AvailableUpdateChanged;

    /// <summary>
    /// 在用的版本变了（装好、切换、删掉；可能在后台线程）
    /// </summary>
    event Action? VersionChanged;

    /// <summary>
    /// 本机已装的版本，顺带确定在用的那个
    /// </summary>
    Task<IReadOnlyList<EnginePackage>> GetLocalVersionsAsync();

    /// <summary>
    /// 拉远端版本，顺带刷新「有没有新版本」
    /// </summary>
    Task<EngineRemoteVersions> PullRemoteVersionsAsync();

    /// <summary>
    /// 选中一个版本并落盘
    /// </summary>
    void Select(EnginePackage version);

    /// <summary>
    /// 安装一个已下好的包
    /// </summary>
    Task InstallAsync(EnginePackage version);

    /// <summary>
    /// 把一个包排进全局下载队列，装好后选中
    /// </summary>
    DownloadJob Enqueue(EnginePackage version);

    /// <summary>
    /// 本机没有引擎、队列里也没有时，把推荐变体的最新包排进队列（会联网）
    /// </summary>
    Task<(EEngineEnsureState State, EnginePackage? Version, DownloadJob? Job)> EnsureQueuedAsync(
        CancellationToken token = default);

    /// <summary>
    /// 拉一次远端看有没有新版本；拉不到不抛
    /// </summary>
    Task CheckForUpdateAsync();

    /// <summary>
    /// 版本的发布页；认不出为 null
    /// </summary>
    string? ReleaseUrl(EnginePackage version);
}

/// <summary>
/// 引擎的更新通道（预览 / 正式）
/// </summary>
public interface IEngineUpdateChannels
{
    /// <summary>
    /// 走正式通道
    /// </summary>
    bool UseStableChannel { get; set; }
}
