/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Interfaces;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.LLamaCpp;
using UiharuMind.Core.LLamaCpp.Data;
using UiharuMind.Core.LLamaCpp.Versions;

namespace UiharuMind.Core.AI.Runtime.Providers;

public sealed class LLamaCppRuntimeProvider(
    LLamaCppServerKernal server,
    Func<VersionInfo?> selectedVersionProvider) : IModelRuntimeProvider
{
    public const string ProviderId = "LLamaCpp";

    public string Id => ProviderId;
    public string DisplayName => "llama.cpp";
    public IReadOnlySet<RuntimeCapability> Capabilities { get; } =
        new HashSet<RuntimeCapability> { RuntimeCapability.Chat };

    public bool CanHandleChat(ILlmModel model)
    {
        return model is GGufModelInfo;
    }

    public bool CanHandleEmbedding(EmbeddingModelSettingConfig settings) => false;

    public async Task<IReadOnlyDictionary<string, ILlmModel>> DiscoverModelsAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, GGufModelInfo> models =
            await server.GetModelList(selectedVersionProvider()).ConfigureAwait(false);
        return models.ToDictionary(x => x.Key, x => (ILlmModel)x.Value);
    }

    public async Task RunChatAsync(
        ModelRuntimeRequest request,
        Action<float>? onLoading,
        Action<IChatClient>? onLoadOver,
        CancellationToken cancellationToken)
    {
        VersionInfo? version = selectedVersionProvider();
        if (version == null)
        {
            Log.Error(
                "Current selected local runtime backend is null. Please select a runtime engine version first.");
            return;
        }

        ApplyResolvedParameters(request.Parameters);
        await server.Run(version, request.Model, onLoading, onLoadOver, token: cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IEmbeddingSession> CreateEmbeddingSessionAsync(
        EmbeddingRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("llama.cpp embedding runtime is not enabled in the provider path.");
    }

    private void ApplyResolvedParameters(RuntimeResolvedParameters parameters)
    {
        LLamaCppSettingConfig config = server.Config;
        config.ParamsConfig.CtxSize = parameters.ContextSize;
        config.ParamsConfig.BatchSize = parameters.BatchSize;
        config.ParamsConfig.UbatchSize = parameters.UBatchSize;
        config.GeneralConfig.GpuLayers = parameters.GpuLayers;
        config.GeneralConfig.FlashAttn = parameters.FlashAttention;
        config.Save();
    }
}
