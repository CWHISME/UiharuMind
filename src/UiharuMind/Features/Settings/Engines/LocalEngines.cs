using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Features.Settings.Engines;

/// <summary>
/// 应用里的本地引擎：设置入口的更新提示点、启动时检查更新都遍历这一份，加引擎只加一行
/// </summary>
public static class LocalEngines
{
    /// <summary>
    /// 全部本地引擎（走应用档案的那一份）
    /// </summary>
    public static IReadOnlyList<IEngineDistribution> All =>
        [LLamaCppEngine.Distribution, AudioCppEngine.Shared.Distribution, SdCppEngine.Shared.Distribution];

    /// <summary>
    /// 装过的引擎各去问一次有没有新版本；没装过的不去问 GitHub
    /// </summary>
    public static async Task CheckForUpdatesAsync()
    {
        foreach (IEngineDistribution engine in All)
        {
            if ((await engine.GetLocalVersionsAsync().ConfigureAwait(false)).Any(x => x.IsInstalled))
                await engine.CheckForUpdateAsync().ConfigureAwait(false);
        }
    }
}
