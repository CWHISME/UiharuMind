using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// llama.cpp 引擎包的下载与安装
/// </summary>
public sealed class LLamaCppEngineInstaller(
    DownloadQueue queue,
    string engineRoot,
    Func<Task<IReadOnlyList<VersionInfo>>> localVersions,
    Func<Task<IReadOnlyList<VersionInfo>>> pullVersions,
    Func<VersionInfo, CancellationToken, Task> install,
    Action<VersionInfo> select)
    : EngineInstaller<VersionInfo>("llama.cpp", queue, engineRoot, localVersions, pullVersions, install, select)
{
    /// <summary>
    /// 走全局队列与 <see cref="LlmManager"/>
    /// </summary>
    public static LLamaCppEngineInstaller Shared { get; } = new(
        DownloadQueue.Shared,
        LLamaCppRuntimeService.EngineRoot,
        async () => (await LlmManager.Instance.GetLocalRuntimeVersions().ConfigureAwait(false)).VersionsList,
        async () => (await LlmManager.Instance.PullLatestRuntimeVersion().ConfigureAwait(false)).VersionsList,
        LlmManager.Instance.InstallRuntimeVersionAsync,
        LlmManager.Instance.SetSelectedRuntimeVersion);
}
