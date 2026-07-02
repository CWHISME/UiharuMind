/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.RemoteOpenAI;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Runtime;

public sealed class ModelRuntimeService
{
    private readonly Func<VersionInfo?> _selectedVersionProvider;
    private readonly Dictionary<string, ModelRunningData> _modelCache = new();
    private readonly List<string> _deleteCache = [];

    public ModelRuntimeBackendRegistry Registry { get; } = new();
    public LLamaCppServerKernal LLamaCppServer { get; }
    public RemoteModelManager RemoteModelManager { get; }
    public IReadOnlyDictionary<string, ModelRunningData> ModelCache => _modelCache;

    public ModelRuntimeService(
        LLamaCppServerKernal llamaCppServer,
        RemoteModelManager remoteModelManager,
        Func<VersionInfo?> selectedVersionProvider)
    {
        LLamaCppServer = llamaCppServer;
        RemoteModelManager = remoteModelManager;
        _selectedVersionProvider = selectedVersionProvider;
        Registry.Register(new LLamaSharpRuntimeBackend());
        Registry.Register(new LLamaCppRuntimeBackend(LLamaCppServer, _selectedVersionProvider));
        Registry.Register(new OpenAICompatibleRuntimeBackend(RemoteModelManager));
    }

    public async Task<IReadOnlyDictionary<string, ModelRunningData>> RefreshModelsAsync(
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, ILlmModel> discovered = new(StringComparer.Ordinal);
        foreach (IModelRuntimeBackend provider in Registry.Backends)
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

            _modelCache[key] = new ModelRunningData(model);
        }

        return _modelCache;
    }

    public async Task<IReadOnlyDictionary<string, ModelRunningData>> RefreshLocalModelsAsync(
        CancellationToken cancellationToken = default)
    {
        IModelRuntimeBackend provider = Registry.GetRequired(LLamaCppRuntimeBackend.ProviderId);
        IReadOnlyDictionary<string, ILlmModel> models =
            await provider.DiscoverModelsAsync(cancellationToken).ConfigureAwait(false);
        return models.ToDictionary(
            x => x.Key,
            x => new ModelRunningData(x.Value));
    }

    public async Task<bool> StartChatModelAsync(
        ModelRunningData runningData,
        Action<float>? onLoading = null,
        Action? onLoaded = null)
    {
        if (runningData.IsRunning) return true;

        CancellationToken token = runningData.BeginLoading();
        bool loaded = false;
        try
        {
            await Run(runningData.ModelInfo, loadingPercent =>
            {
                runningData.UpdateLoading(loadingPercent);
                onLoading?.Invoke(loadingPercent);
            }, chatClient =>
            {
                runningData.CompleteLoading(chatClient);
                loaded = true;
                onLoaded?.Invoke();
            }, token).ConfigureAwait(false);

            return loaded;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
            return false;
        }
        finally
        {
            if (!loaded) runningData.FailLoading();
        }
    }

    public async Task Run(
        ILlmModel model,
        Action<float>? onLoading = null,
        Action<IChatClient>? onLoadOver = null,
        CancellationToken token = default)
    {
        ModelRuntimeSettingConfig settings = ModelRuntimeSettingConfig.Current;
        IModelRuntimeBackend? provider = Registry.FindChatBackend(model, GetPreferredChatProviderId(model, settings));
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
        IModelRuntimeBackend? provider = Registry.FindChatBackend(model, GetPreferredChatProviderId(model, settings));
        if (provider == null || model is RemoteModelInfo) return RuntimeLoadRisk.Low;

        ModelMetadata metadata = ModelMetadataService.Read(model);
        RuntimeParameterPolicy policy = CreateChatParameterPolicy(provider, settings);
        RuntimeResolvedParameters parameters = RuntimeParameterResolver.Resolve(settings, metadata, policy);
        return RuntimeLoadRiskEvaluator.Evaluate(model, metadata, parameters, policy, RuntimeDeviceInfoProvider.Capture());
    }

    private static string? GetPreferredChatProviderId(ILlmModel model, ModelRuntimeSettingConfig settings)
    {
        if (model is RemoteModelInfo) return OpenAICompatibleRuntimeBackend.ProviderId;
        return settings.EngineType switch
        {
            ModelRuntimeSettingConfig.EngineLLamaCpp => LLamaCppRuntimeBackend.ProviderId,
            ModelRuntimeSettingConfig.EngineLLamaSharp => LLamaSharpRuntimeBackend.ProviderId,
            _ => null
        };
    }

    public async Task<IEmbeddingSession> CreateEmbeddingSessionAsync(
        EmbeddingModelSettingConfig settings,
        string modelPath,
        CancellationToken cancellationToken)
    {
        IModelRuntimeBackend? provider = Registry.FindEmbeddingBackend(settings);
        if (provider == null)
            throw new EmbeddingRuntimeException($"No embedding provider can handle backend '{settings.Backend}'.");

        ModelMetadata metadata = File.Exists(modelPath)
            ? ModelMetadataService.FromGGufMetadata(GGufMetadataReader.TryRead(modelPath))
            : ModelMetadata.Empty;
        RuntimeResolvedParameters parameters = ResolveEmbeddingParameters(settings, metadata);
        EmbeddingRuntimeRequest request = new(settings, modelPath, metadata, parameters);
        return await provider.CreateEmbeddingSessionAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static RuntimeParameterPolicy CreateChatParameterPolicy(
        IModelRuntimeBackend provider,
        ModelRuntimeSettingConfig settings)
    {
        return provider.Id switch
        {
            LLamaSharpRuntimeBackend.ProviderId => LLamaSharpRuntimeEngine.CreatePolicy(settings),
            LLamaCppRuntimeBackend.ProviderId => new RuntimeParameterPolicy(
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
