using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Features.Settings.Engines;

/// <summary>
/// 拉一次远端版本的结果
/// </summary>
/// <param name="Versions">版本列表（含本机已有的）</param>
/// <param name="ReleaseInfo">最新版本说明（markdown）；拉不到为 null</param>
public sealed record EngineRemoteVersions(IReadOnlyList<EnginePackage> Versions, string? ReleaseInfo);

/// <summary>
/// 引擎版本卡背后的那个引擎：本机版本、远端版本、选中、安装、更新提示。
/// 卡片只认这一层，llama.cpp、audio.cpp（以后的本地生图引擎）各实现一份
/// </summary>
public interface IEngineVersionSource
{
    /// <summary>
    /// 引擎名，卡片标题与提示里用
    /// </summary>
    string EngineName { get; }

    /// <summary>
    /// 卡片标题前的图标（Assets/Icons 下的名字）
    /// </summary>
    string IconName { get; }

    /// <summary>
    /// 版本下拉的说明：选中的版本用在哪
    /// </summary>
    string VersionDescription { get; }

    /// <summary>
    /// 引擎包所在目录（打开目录按钮）
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
    /// 有没有更新通道可选（预览 / 正式）
    /// </summary>
    bool HasChannels { get; }

    /// <summary>
    /// 走正式通道；没有通道的引擎忽略
    /// </summary>
    bool UseStableChannel { get; set; }

    /// <summary>
    /// 装好了一个包（可能在下载线程上）
    /// </summary>
    event Action? Installed;

    /// <summary>
    /// 有没有新版本变了（可能在后台线程）
    /// </summary>
    event Action? AvailableUpdateChanged;

    /// <summary>
    /// 本机已装的版本
    /// </summary>
    Task<IReadOnlyList<EnginePackage>> GetLocalVersionsAsync();

    /// <summary>
    /// 拉远端版本，顺带刷新「有没有新版本」
    /// </summary>
    Task<EngineRemoteVersions> PullRemoteVersionsAsync();

    /// <summary>
    /// 选中一个版本
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
    /// 版本的发布页；认不出为 null
    /// </summary>
    string? ReleaseUrl(EnginePackage version);
}
