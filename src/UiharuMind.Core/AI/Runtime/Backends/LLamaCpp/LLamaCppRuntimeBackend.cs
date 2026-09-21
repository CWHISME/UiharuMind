/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Runtime.Backends;

internal sealed class LLamaCppRuntimeBackend(
    LLamaCppRuntimeService server,
    Func<VersionInfo?> selectedVersionProvider) : IModelRuntimeBackend
{
    public const string BackendId = "LLamaCpp";

    public string Id => BackendId;
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
               string.Equals(settings.Backend, BackendId, StringComparison.OrdinalIgnoreCase);
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

        await server.Run(version, request.Model, request.Parameters, onLoading, onLoadOver, token: cancellationToken)
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
            LLamaCppSettingConfig.Current,
            request.ModelPath,
            request.Parameters,
            cancellationToken).ConfigureAwait(false);
    }

}
