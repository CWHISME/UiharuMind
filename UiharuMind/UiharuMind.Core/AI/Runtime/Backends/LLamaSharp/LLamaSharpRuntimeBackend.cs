/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Runtime.Backends;

public sealed class LLamaSharpRuntimeBackend : IModelRuntimeBackend
{
    public const string ProviderId = "LLamaSharp";
    private readonly LLamaSharpRuntimeEngine _engine = new();

    public string Id => ProviderId;
    public string DisplayName => "LLamaSharp";
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

    public Task<IReadOnlyDictionary<string, ILlmModel>> DiscoverModelsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<string, ILlmModel>>(
            new Dictionary<string, ILlmModel>());
    }

    public Task RunChatAsync(
        ModelRuntimeRequest request,
        Action<float>? onLoading,
        Action<IChatClient>? onLoadOver,
        CancellationToken cancellationToken)
    {
        return _engine.Run(
            request.Model,
            request.Parameters,
            request.Settings,
            onLoading,
            onLoadOver,
            cancellationToken);
    }

    public async Task<IEmbeddingSession> CreateEmbeddingSessionAsync(
        EmbeddingRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        return await LLamaSharpEmbeddingSession.CreateAsync(
            request.ModelPath,
            request.Parameters.ContextSize,
            request.Parameters.BatchSize,
            request.Parameters.UBatchSize,
            request.Parameters.GpuLayers,
            cancellationToken).ConfigureAwait(false);
    }
}
