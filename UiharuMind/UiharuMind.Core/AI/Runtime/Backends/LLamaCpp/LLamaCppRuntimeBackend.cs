/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.AI.Runtime.Backends;

public sealed class LLamaCppRuntimeBackend(
    LLamaCppServerKernal server,
    Func<VersionInfo?> selectedVersionProvider) : IModelRuntimeBackend
{
    public const string ProviderId = "LLamaCpp";

    public string Id => ProviderId;
    public string DisplayName => "llama.cpp";
    public IReadOnlySet<RuntimeCapability> Capabilities { get; } =
        new HashSet<RuntimeCapability> { RuntimeCapability.Chat, RuntimeCapability.Embedding };

    public bool CanHandleChat(ILlmModel model)
    {
        return model is GGufModelInfo;
    }

    public bool CanHandleEmbedding(EmbeddingModelSettingConfig settings)
    {
        return !EmbeddingModelResolver.IsRemote(settings) &&
               string.Equals(settings.Backend, ProviderId, StringComparison.OrdinalIgnoreCase);
    }

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

    public async Task<IEmbeddingSession> CreateEmbeddingSessionAsync(
        EmbeddingRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        VersionInfo? version = selectedVersionProvider();
        if (version == null)
            throw new EmbeddingRuntimeException("llama.cpp runtime is not selected.");

        return await LLamaCppEmbeddingSession.StartAsync(
            version,
            server.Config,
            request.ModelPath,
            request.Parameters,
            cancellationToken).ConfigureAwait(false);
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
