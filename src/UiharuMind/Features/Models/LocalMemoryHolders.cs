using System;
using System.Collections.Generic;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models;

/// <summary>
/// 此刻占着内存、可以先放掉的一个本地模型或引擎
/// </summary>
/// <param name="Name">给用户看的名字</param>
/// <param name="Release">放掉</param>
public sealed record LocalMemoryHolder(string Name, Action Release);

/// <summary>
/// 本机此刻在跑的其他本地模型：内存不够起新引擎时，让用户挑「先放掉它们再加载」。
/// 只给有人看着的入口用，不自动卸——卸对话模型牵动「全局当前模型」（ADR 0077）
/// </summary>
public static class LocalMemoryHolders
{
    /// <summary>
    /// 在跑的本地对话模型与语音引擎
    /// </summary>
    /// <returns>可放掉的；没有为空</returns>
    public static IReadOnlyList<LocalMemoryHolder> Current()
    {
        List<LocalMemoryHolder> holders = [];
        if (LlmManager.Instance.CurrentRunningModel is { IsRemoteModel: false } chat && (chat.IsRunning || chat.IsLoading))
            holders.Add(new LocalMemoryHolder(chat.ModelName, () => LlmManager.Instance.UnloadModel(chat.ModelName)));
        if (AudioCppEngine.Shared.Runtime.State is EEngineRunState.Running or EEngineRunState.Starting)
            holders.Add(new LocalMemoryHolder(Loc.Text(LangKey.LocalHolderAudioEngine), AudioCppEngine.Shared.Stop));
        return holders;
    }
}
