using System;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// GGUF 仓库分类要的外部动作：下模型时顺带装 llama.cpp、下完用上
/// </summary>
public sealed record GgufMarketHooks
{
    /// <summary>
    /// 本机没有引擎时把推荐包排在模型前面（会联网，别在界面线程等）
    /// </summary>
    public Func<Task<(EEngineEnsureState State, EnginePackage? Version, DownloadJob? Job)>> EnsureEngine { get; init; } =
        () => LLamaCppEngine.Distribution.EnsureQueuedAsync();

    /// <summary>
    /// 加载某个本地模型（与顶栏选模型同一条路，带风险确认）
    /// </summary>
    public Func<string, Task> UseModel { get; init; } = _ => Task.CompletedTask;

    /// <summary>
    /// 当前已选了模型（在跑或在加载）
    /// </summary>
    public Func<bool> HasCurrentModel { get; init; } = () => true;
}
