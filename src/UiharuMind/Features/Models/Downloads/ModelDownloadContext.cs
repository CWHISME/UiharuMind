using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 「获取模型」各层共用的依赖。默认值走全局队列与真实模型目录，测试按需替换
/// </summary>
/// <param name="Messages">提示与通知</param>
/// <param name="QueueView">下载区</param>
/// <param name="RefreshLocalModels">刷新本地模型列表</param>
/// <param name="OpenSourceSettings">跳到下载源设置</param>
public sealed record ModelDownloadContext(
    IMessageService Messages,
    DownloadQueueViewData QueueView,
    Func<Task> RefreshLocalModels,
    Action OpenSourceSettings)
{
    /// <summary>
    /// 当前设置对应的模型源
    /// </summary>
    public Func<IModelSource> CreateSource { get; init; } = () => ModelSources.Create();

    /// <summary>
    /// 下载队列
    /// </summary>
    public DownloadQueue Queue { get; init; } = DownloadQueue.Shared;

    /// <summary>
    /// 按量化排队
    /// </summary>
    public ModelRepoDownloader Downloader { get; init; } = ModelRepoDownloader.Shared;

    /// <summary>
    /// 模型目录
    /// </summary>
    public Func<string> ModelRoot { get; init; } = () => ModelSettingConfig.Current.LocalModelPath;

    /// <summary>
    /// 本地已有模型：模型标识 → 路径（会扫盘，别在界面线程调）
    /// </summary>
    public Func<IReadOnlyDictionary<string, string>> LocalModels { get; init; } = () =>
        LocalModelScanner.Scan()
            .GroupBy(x => x.Info.ModelName, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Info.ModelPath, StringComparer.Ordinal);

    /// <summary>
    /// 本机没有引擎时把推荐包排在模型前面（会联网，别在界面线程等）
    /// </summary>
    public Func<Task<(EEngineEnsureState State, VersionInfo? Version, DownloadJob? Job)>> EnsureEngine { get; init; } =
        () => LLamaCppEngineInstaller.Shared.EnsureQueuedAsync();

    /// <summary>
    /// 本机设备信息
    /// </summary>
    public Func<RuntimeDeviceInfo> DeviceInfo { get; init; } = () => RuntimeDeviceInfoProvider.Capture();
}
