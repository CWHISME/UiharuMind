/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Interfaces;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.AI.LocalAI.LLamaSharp;
using UiharuMind.Core.AI.Runtime.Providers;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.LLamaCpp;
using UiharuMind.Core.LLamaCpp.Data;
using UiharuMind.Core.LLamaCpp.Versions;
using UiharuMind.Core.RemoteOpenAI;

namespace UiharuMind.Core.AI.Runtime;

public sealed class ModelRuntimeCoordinator : ILlmRuntime
{
    private readonly Func<VersionInfo?> _selectedVersionProvider;
    private readonly Dictionary<string, ModelRunningData> _modelCache = new();
    private readonly List<string> _deleteCache = [];

    public RuntimeProviderRegistry Registry { get; } = new();
    public LLamaCppServerKernal LLamaCppServer { get; }
    public RemoteModelManager RemoteModelManager { get; }
    public IReadOnlyDictionary<string, ModelRunningData> ModelCache => _modelCache;

    public ModelRuntimeCoordinator(
        LLamaCppServerKernal llamaCppServer,
        RemoteModelManager remoteModelManager,
        Func<VersionInfo?> selectedVersionProvider)
    {
        LLamaCppServer = llamaCppServer;
        RemoteModelManager = remoteModelManager;
        _selectedVersionProvider = selectedVersionProvider;
        Registry.Register(new LLamaSharpRuntimeProvider());
        Registry.Register(new LLamaCppRuntimeProvider(LLamaCppServer, _selectedVersionProvider));
        Registry.Register(new OpenAICompatibleRuntimeProvider(RemoteModelManager));
    }

    public async Task<IReadOnlyDictionary<string, ModelRunningData>> RefreshModelsAsync(
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, ILlmModel> discovered = new(StringComparer.Ordinal);
        foreach (IModelRuntimeProvider provider in Registry.Providers)
        {
            IReadOnlyDictionary<string, ILlmModel> providerModels =
                await provider.DiscoverModelsAsync(cancellationToken).ConfigureAwait(false);
            foreach ((string key, ILlmModel model) in providerModels)
                discovered[key] = model;
        }

        _deleteCache.Clear();
        foreach (string cachedKey in _modelCache.Keys)
        {
            if (!discovered.ContainsKey(cachedKey)) _deleteCache.Add(cachedKey);
        }

        foreach (string key in _deleteCache)
            _modelCache.Remove(key);

        foreach ((string key, ILlmModel model) in discovered)
        {
            if (_modelCache.TryGetValue(key, out ModelRunningData? runningData))
            {
                runningData.ForceUpdateModelInfo(model);
                continue;
            }

            _modelCache[key] = new ModelRunningData(this, model);
        }

        return _modelCache;
    }

    public async Task<IReadOnlyDictionary<string, ModelRunningData>> RefreshLocalModelsAsync(
        CancellationToken cancellationToken = default)
    {
        IModelRuntimeProvider provider = Registry.GetRequired(LLamaCppRuntimeProvider.ProviderId);
        IReadOnlyDictionary<string, ILlmModel> models =
            await provider.DiscoverModelsAsync(cancellationToken).ConfigureAwait(false);
        return models.ToDictionary(
            x => x.Key,
            x => new ModelRunningData(this, x.Value));
    }

    public async Task Run(
        ILlmModel model,
        Action<float>? onLoading = null,
        Action<IChatClient>? onLoadOver = null,
        CancellationToken token = default)
    {
        ModelRuntimeSettingConfig settings = ModelRuntimeSettingConfig.Current;
        IModelRuntimeProvider? provider = Registry.FindChatProvider(model, GetPreferredChatProviderId(model, settings));
        if (provider == null)
        {
            Log.Error($"No runtime provider can handle model '{model.ModelName}'.");
            return;
        }

        ModelMetadata metadata = ModelMetadataService.Read(model);
        RuntimeParameterPolicy policy = CreateChatParameterPolicy(provider, settings);
        RuntimeResolvedParameters parameters = RuntimeParameterResolver.Resolve(settings, metadata, policy);
        RuntimeLoadRisk risk = model is RemoteModelInfo
            ? RuntimeLoadRisk.Low
            : RuntimeLoadRiskEvaluator.Evaluate(model, metadata, parameters, policy, RuntimeDeviceInfoProvider.Capture());
        ModelRuntimeRequest request = new(model, settings, metadata, parameters, risk);
        await provider.RunChatAsync(request, onLoading, onLoadOver, token).ConfigureAwait(false);
    }

    public RuntimeLoadRisk AnalyzeChatLoadRisk(ILlmModel model)
    {
        ModelRuntimeSettingConfig settings = ModelRuntimeSettingConfig.Current;
        IModelRuntimeProvider? provider = Registry.FindChatProvider(model, GetPreferredChatProviderId(model, settings));
        if (provider == null || model is RemoteModelInfo) return RuntimeLoadRisk.Low;

        ModelMetadata metadata = ModelMetadataService.Read(model);
        RuntimeParameterPolicy policy = CreateChatParameterPolicy(provider, settings);
        RuntimeResolvedParameters parameters = RuntimeParameterResolver.Resolve(settings, metadata, policy);
        return RuntimeLoadRiskEvaluator.Evaluate(model, metadata, parameters, policy, RuntimeDeviceInfoProvider.Capture());
    }

    private static string? GetPreferredChatProviderId(ILlmModel model, ModelRuntimeSettingConfig settings)
    {
        if (model is RemoteModelInfo) return OpenAICompatibleRuntimeProvider.ProviderId;
        return settings.EngineType switch
        {
            ModelRuntimeSettingConfig.EngineLLamaCpp => LLamaCppRuntimeProvider.ProviderId,
            ModelRuntimeSettingConfig.EngineLLamaSharp => LLamaSharpRuntimeProvider.ProviderId,
            _ => null
        };
    }

    public async Task<IEmbeddingSession> CreateEmbeddingSessionAsync(
        EmbeddingModelSettingConfig settings,
        string modelPath,
        CancellationToken cancellationToken)
    {
        IModelRuntimeProvider? provider = Registry.FindEmbeddingProvider(settings);
        if (provider == null)
            throw new EmbeddingRuntimeException($"No embedding provider can handle backend '{settings.Backend}'.");

        ModelMetadata metadata = File.Exists(modelPath)
            ? ModelMetadataService.FromGGufMetadata(LocalAI.GGuf.GGufMetadataReader.TryRead(modelPath))
            : ModelMetadata.Empty;
        RuntimeResolvedParameters parameters = ResolveEmbeddingParameters(settings, metadata);
        EmbeddingRuntimeRequest request = new(settings, modelPath, metadata, parameters);
        return await provider.CreateEmbeddingSessionAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static RuntimeParameterPolicy CreateChatParameterPolicy(
        IModelRuntimeProvider provider,
        ModelRuntimeSettingConfig settings)
    {
        return provider.Id switch
        {
            LLamaSharpRuntimeProvider.ProviderId => LLamaSharpRuntimeEngine.CreatePolicy(settings),
            LLamaCppRuntimeProvider.ProviderId => new RuntimeParameterPolicy(
                settings.GpuLayers <= 0 ? RuntimeDeviceMode.Cpu : RuntimeDeviceMode.Auto,
                settings.GpuLayers > 0,
                false),
            _ => new RuntimeParameterPolicy(RuntimeDeviceMode.Cpu, false, true)
        };
    }

    private static RuntimeResolvedParameters ResolveEmbeddingParameters(
        EmbeddingModelSettingConfig settings,
        ModelMetadata metadata)
    {
        ModelRuntimeSettingConfig normalized = new()
        {
            ContextSize = Math.Max(0, settings.ContextSize),
            BatchSize = Math.Max(0, settings.BatchSize),
            UBatchSize = Math.Max(0, settings.UBatchSize),
            GpuLayers = settings.GpuLayers,
            Threads = 0,
            FlashAttention = false,
            LLamaSharpBackendMode = ModelRuntimeSettingConfig.LLamaSharpBackendAuto
        };

        RuntimeParameterPolicy policy = new(
            settings.GpuLayers <= 0 ? RuntimeDeviceMode.Cpu : RuntimeDeviceMode.Auto,
            settings.GpuLayers > 0,
            false);
        return RuntimeParameterResolver.Resolve(normalized, metadata, policy);
    }
}
