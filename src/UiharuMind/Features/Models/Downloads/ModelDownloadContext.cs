using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Shared.Services;
using UiharuMind.Core.AI.Engines;

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
    private AudioCppEngine? _audioEngine;
    private AudioCppPackageStore? _audioStore;
    private LocalAudioDefaults? _audioDefaults;

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
        () => LLamaCppEngine.Shared.Installer.EnsureQueuedAsync();

    /// <summary>
    /// 本机设备信息
    /// </summary>
    public Func<RuntimeDeviceInfo> DeviceInfo { get; init; } = () => RuntimeDeviceInfoProvider.Capture();

    /// <summary>
    /// 加载某个本地模型（与顶栏选模型同一条路，带风险确认）
    /// </summary>
    public Func<string, Task> UseModel { get; init; } = _ => Task.CompletedTask;

    /// <summary>
    /// 当前已选了模型（在跑或在加载）
    /// </summary>
    public Func<bool> HasCurrentModel { get; init; } = () => true;

    /// <summary>
    /// 语音引擎（语音模型清单随它的 spec 走）。没切到语音分类就不碰共享实例
    /// </summary>
    public AudioCppEngine AudioEngine
    {
        get => _audioEngine ??= AudioCppEngine.Shared;
        init => _audioEngine = value;
    }

    /// <summary>
    /// 朗读 / 听写默认用本机哪个模型（与「语音模型」页签同一口径）
    /// </summary>
    public LocalAudioDefaults AudioDefaults
    {
        get => _audioDefaults ??= new LocalAudioDefaults(AudioEngine, AudioRouter.Shared);
        init => _audioDefaults = value;
    }

    /// <summary>
    /// 语音模型包的下载与删除
    /// </summary>
    public AudioCppPackageStore AudioStore
    {
        get => _audioStore ??= AudioCppPackageStore.Shared;
        init => _audioStore = value;
    }
}
