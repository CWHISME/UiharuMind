/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Runtime;

public interface IModelRuntimeBackend
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlySet<RuntimeCapability> Capabilities { get; }

    bool CanHandleChat(ILlmModel model);
    bool CanHandleEmbedding(EmbeddingModelSettingConfig settings);

    Task<IReadOnlyDictionary<string, ILlmModel>> DiscoverModelsAsync(CancellationToken cancellationToken = default);

    Task RunChatAsync(
        ModelRuntimeRequest request,
        Action<float>? onLoading,
        Action<IChatClient>? onLoadOver,
        CancellationToken cancellationToken);

    Task<IEmbeddingSession> CreateEmbeddingSessionAsync(
        EmbeddingRuntimeRequest request,
        CancellationToken cancellationToken);
}

internal sealed class ModelRuntimeBackendRegistry
{
    private readonly List<IModelRuntimeBackend> _backends = [];

    public IReadOnlyList<IModelRuntimeBackend> Backends => _backends;

    public void Register(IModelRuntimeBackend backend)
    {
        if (_backends.Any(x => x.Id == backend.Id))
            throw new InvalidOperationException($"Runtime backend '{backend.Id}' is already registered.");
        _backends.Add(backend);
    }

    public IModelRuntimeBackend GetRequired(string backendId)
    {
        return _backends.FirstOrDefault(x => x.Id == backendId)
               ?? throw new InvalidOperationException($"Runtime backend '{backendId}' is not registered.");
    }

    public IModelRuntimeBackend? FindChatBackend(ILlmModel model, string? preferredBackendId = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredBackendId))
        {
            IModelRuntimeBackend? preferred = _backends.FirstOrDefault(x => x.Id == preferredBackendId);
            if (preferred?.CanHandleChat(model) == true) return preferred;
        }

        return _backends.FirstOrDefault(x => x.CanHandleChat(model));
    }

    public IModelRuntimeBackend? FindEmbeddingBackend(EmbeddingModelSettingConfig settings)
    {
        return _backends.FirstOrDefault(x => x.CanHandleEmbedding(settings));
    }
}
