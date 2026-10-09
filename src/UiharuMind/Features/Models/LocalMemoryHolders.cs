using System;
using System.Collections.Generic;
using System.Linq;
using UiharuMind.Features.Models.Usage;

namespace UiharuMind.Features.Models;

/// <summary>
/// 此刻占着内存、可以先放掉的一个本地模型或引擎
/// </summary>
/// <param name="Name">给用户看的名字</param>
/// <param name="Release">放掉</param>
public sealed record LocalMemoryHolder(string Name, Action Release);

/// <summary>
/// 起生图引擎内存不够时，让用户挑「先放掉它们再加载」的其他本地模型。
/// 只给有人看着的入口用，不自动卸——卸对话模型牵动「全局当前模型」（ADR 0077）
/// </summary>
public static class LocalMemoryHolders
{
    /// <summary>
    /// 在跑的本地对话模型、知识库嵌入与语音引擎
    /// </summary>
    /// <returns>可放掉的；没有为空</returns>
    public static IReadOnlyList<LocalMemoryHolder> Current() =>
        LocalEngines.Current()
            .Where(x => x.Kind != ELocalEngineKind.Image)
            .Select(x => new LocalMemoryHolder(x.Name, x.Release))
            .ToList();
}
