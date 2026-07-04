/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.RemoteOpenAI;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Runtime.Backends;

internal sealed class OpenAICompatibleRuntimeBackend(RemoteModelManager remoteModelManager) : IModelRuntimeBackend
{
    public const string BackendId = "OpenAICompatible";

    public string Id => BackendId;
    public string DisplayName => "OpenAI Compatible";
    public IReadOnlySet<RuntimeCapability> Capabilities { get; } =
        new HashSet<RuntimeCapability> { RuntimeCapability.Chat, RuntimeCapability.Embedding };

    public bool CanHandleChat(ILlmModel model) => model is RemoteModelInfo;

    public bool CanHandleEmbedding(EmbeddingModelSettingConfig settings)
    {
        return EmbeddingModelResolver.IsRemote(settings);
    }

    public Task<IReadOnlyDictionary<string, ILlmModel>> DiscoverModelsAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, ILlmModel> models = RemoteModelSettingConfig.Current.ModelInfos.ToDictionary(x => x.Key, x => (ILlmModel)x.Value);
        return Task.FromResult(models);
    }

    public Task RunChatAsync(
        ModelRuntimeRequest request,
        Action<float>? onLoading,
        Action<IChatClient>? onLoadOver,
        CancellationToken cancellationToken)
    {
        return remoteModelManager.Run(request.Model, onLoading, onLoadOver, cancellationToken);
    }

    public Task<IEmbeddingSession> CreateEmbeddingSessionAsync(
        EmbeddingRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        IEmbeddingSession session = new OpenAICompatibleEmbeddingSession(
            request.Settings.RemoteEndpoint,
            request.Settings.RemoteModelId,
            request.Settings.RemoteApiKey);
        return Task.FromResult(session);
    }
}
