/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Interfaces;

namespace UiharuMind.Core.AI.Runtime;

public interface IModelRuntimeProvider
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

public sealed class RuntimeProviderRegistry
{
    private readonly List<IModelRuntimeProvider> _providers = [];

    public IReadOnlyList<IModelRuntimeProvider> Providers => _providers;

    public void Register(IModelRuntimeProvider provider)
    {
        if (_providers.Any(x => x.Id == provider.Id))
            throw new InvalidOperationException($"Runtime provider '{provider.Id}' is already registered.");
        _providers.Add(provider);
    }

    public IModelRuntimeProvider GetRequired(string providerId)
    {
        return _providers.FirstOrDefault(x => x.Id == providerId)
               ?? throw new InvalidOperationException($"Runtime provider '{providerId}' is not registered.");
    }

    public IModelRuntimeProvider? FindChatProvider(ILlmModel model, string? preferredProviderId = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredProviderId))
        {
            IModelRuntimeProvider? preferred = _providers.FirstOrDefault(x => x.Id == preferredProviderId);
            if (preferred?.CanHandleChat(model) == true) return preferred;
        }

        return _providers.FirstOrDefault(x => x.CanHandleChat(model));
    }

    public IModelRuntimeProvider? FindEmbeddingProvider(EmbeddingModelSettingConfig settings)
    {
        return _providers.FirstOrDefault(x => x.CanHandleEmbedding(settings));
    }
}
