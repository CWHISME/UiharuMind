using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.Configs;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 本地引擎的种类
/// </summary>
public enum ELocalEngineKind
{
    Chat,
    Embedding,
    Image,
    Audio
}

/// <summary>
/// 此刻在本机跑着（或正加载）的一个本地模型进程
/// </summary>
/// <param name="Kind">种类</param>
/// <param name="Name">给用户看的名字：对话模型为模型名，其余为引擎名</param>
/// <param name="LoadProgress">加载进度（0~1）；已在跑为 null</param>
/// <param name="SampleUsage">取一次实际占用（会碰进程，别在界面线程上调）</param>
/// <param name="Release">停掉，内存还回去</param>
public sealed record LocalEngineEntry(
    ELocalEngineKind Kind,
    string Name,
    float? LoadProgress,
    Func<EngineUsage?> SampleUsage,
    Action Release);

/// <summary>
/// 本机所有在跑的本地模型进程：对话（每个模型一个 llama-server）、知识库嵌入、生图、语音。
/// 只看状态不碰进程，界面线程上调
/// </summary>
public static class LocalEngines
{
    /// <summary>
    /// 此刻在跑或正加载的
    /// </summary>
    /// <returns>没有为空</returns>
    public static IReadOnlyList<LocalEngineEntry> Current()
    {
        List<LocalEngineEntry> entries = [];
        LlmManager llm = LlmManager.Instance;
        foreach (ModelRunningData model in llm.CacheModelDictionary.Values)
        {
            if (model.IsRemoteModel || !(model.IsRunning || model.IsLoading)) continue;
            string name = model.ModelName;
            entries.Add(new LocalEngineEntry(ELocalEngineKind.Chat, name,
                model.IsLoading ? model.LoadingPercent : null,
                () => llm.SampleUsage(name), () => llm.UnloadModel(name)));
        }

        EmbeddingModelService embedding = EmbeddingModelService.Instance;
        if (embedding.IsRunning && !EmbeddingModelResolver.IsRemote(ConfigManager.Instance.EmbeddingModelSetting))
            // 停会话要等它的锁，正在起的话会等到起完，放到后台
            entries.Add(new LocalEngineEntry(ELocalEngineKind.Embedding, Loc.Text(LangKey.LocalHolderEmbedding), null,
                embedding.SampleUsage, () => Task.Run(embedding.StopSession)));

        AddEngine(entries, ELocalEngineKind.Image, LangKey.LocalHolderImageEngine, SdCppEngine.Shared.Runtime);
        AddEngine(entries, ELocalEngineKind.Audio, LangKey.LocalHolderAudioEngine, AudioCppEngine.Shared.Runtime);
        return entries;
    }

    private static void AddEngine(List<LocalEngineEntry> entries, ELocalEngineKind kind, LangKey name,
        IEngineRuntime runtime)
    {
        if (runtime.State is not (EEngineRunState.Running or EEngineRunState.Starting)) return;
        entries.Add(new LocalEngineEntry(kind, Loc.Text(name),
            runtime.State == EEngineRunState.Starting ? runtime.LoadProgress : null,
            runtime.SampleUsage, runtime.Stop));
    }
}
