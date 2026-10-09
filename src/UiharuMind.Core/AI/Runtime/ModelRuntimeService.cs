/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.RemoteOpenAI;

namespace UiharuMind.Core.AI.Runtime;

internal sealed class ModelRuntimeService
{
    private readonly Dictionary<string, ModelRunningData> _modelCache = new();
    private readonly List<string> _deleteCache = [];
    private readonly ModelRuntimeBackendRegistry _registry = new();

    public IReadOnlyDictionary<string, ModelRunningData> ModelCache => _modelCache;

    /// <summary>
    /// 已注册的本地引擎，多于一个时界面才让用户选
    /// </summary>
    public IReadOnlyList<IModelRuntimeBackend> LocalEngines => _registry.LocalEngines;

    public ModelRuntimeService(
        LLamaCppRuntimeService llamaCppServer,
        RemoteModelManager remoteModelManager,
        Func<CancellationToken, Task<VersionInfo?>> currentLLamaCppVersion)
    {
        // LLamaSharp 已屏蔽（ADR 0067），代码留待移除
        _registry.Register(new LLamaCppRuntimeBackend(llamaCppServer, currentLLamaCppVersion));
        _registry.Register(new OpenAICompatibleRuntimeBackend(remoteModelManager));
    }

    public async Task<IReadOnlyDictionary<string, ModelRunningData>> RefreshModelsAsync(
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, ILlmModel> discovered = new(StringComparer.Ordinal);
        foreach (LocalModelEntry entry in LocalModelScanner.Scan(ELocalModelKind.Chat))
            discovered[entry.Info.ModelName] = entry.Info;
        foreach (IModelRuntimeBackend backend in _registry.Backends)
        {
            IReadOnlyDictionary<string, ILlmModel> backendModels =
                await backend.DiscoverModelsAsync(cancellationToken).ConfigureAwait(false);
            foreach ((string key, ILlmModel model) in backendModels)
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
            if (!_modelCache.TryGetValue(key, out ModelRunningData? runningData))
                _modelCache[key] = runningData = new ModelRunningData(model);
            else
                runningData.ForceUpdateModelInfo(model);
            runningData.ApplyBackend(_registry.FindChatBackend(model, GetPreferredChatBackendId(model)));
        }

        return _modelCache;
    }

    public async Task<bool> StartChatModelAsync(
        ModelRunningData runningData,
        Action<float>? onLoading = null,
        Action? onLoaded = null)
    {
        if (runningData.IsRunning) return true;
        if (runningData.IsLoading) return false; //别人正在拉起它，再起一份就是双跑

        CancellationToken token = runningData.BeginLoading();
        bool loaded = false;
        int runtimeContextSize = 0; //本地模型的实际加载上下文,供历史预算按真实值开窗
        try
        {
            await Run(runningData.ModelInfo, loadingPercent =>
            {
                runningData.UpdateLoading(loadingPercent, token);
                onLoading?.Invoke(loadingPercent);
            }, chatClient =>
            {
                loaded = runningData.CompleteLoading(chatClient, runtimeContextSize, token);
                onLoaded?.Invoke();
            }, token, parameters => runtimeContextSize = parameters.ContextSize).ConfigureAwait(false);

            return loaded;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
            if (loaded) runningData.FailRunning(e);
            else runningData.FailLoading(e, token);
            return false;
        }
        finally
        {
            if (!loaded && runningData.LastError == null) runningData.FailLoading(loadToken: token);
        }
    }

    /// <param name="onParametersResolved">解析出运行参数时回调。本地模型的实际上下文只有这里知道——
    /// auto 档按可用内存缩放，与元数据标称值可能差一个数量级，历史预算必须用这个真实值</param>
    public async Task Run(
        ILlmModel model,
        Action<float>? onLoading = null,
        Action<IChatClient>? onLoadOver = null,
        CancellationToken token = default,
        Action<RuntimeResolvedParameters>? onParametersResolved = null)
    {
        ModelRuntimeSettingConfig settings = ModelRuntimeSettingConfig.Current.ForModel(model.ModelName);
        IModelRuntimeBackend? backend = _registry.FindChatBackend(model, GetPreferredChatBackendId(model));
        if (backend == null)
            throw model is RemoteModelInfo
                ? new InvalidOperationException($"No runtime backend can handle model '{model.ModelName}'.")
                : new LocalEngineNotReadyException();

        ModelMetadata metadata = ModelMetadataService.Read(model);
        RuntimeParameterPolicy policy = backend.CreateParameterPolicy(settings);
        RuntimeResolvedParameters parameters = RuntimeParameterResolver.Resolve(settings, metadata, policy);
        onParametersResolved?.Invoke(parameters);
        RuntimeLoadRisk risk = model is RemoteModelInfo
            ? RuntimeLoadRisk.Low
            : RuntimeLoadRiskEvaluator.Evaluate(model, metadata, parameters, policy,
                RuntimeDeviceInfoProvider.Capture(waitForGpu: true));
        ModelRuntimeRequest request = new(model, settings, metadata, parameters, risk);
        bool loaded = false;
        try
        {
            await backend.RunChatAsync(request, onLoading, client =>
            {
                loaded = true;
                onLoadOver?.Invoke(client);
            }, token).ConfigureAwait(false);
        }
        // 估算再准也有漏的（别的程序突然占了内存、驱动另有开销），加载时分配失败就把上下文减半再试一次
        catch (LocalServerException e) when (!loaded && !token.IsCancellationRequested && e.IsOutOfMemory &&
                                             RuntimeParameterResolver.HalveContext(parameters) is { } smaller)
        {
            Log.Warning($"{model.ModelName} ran out of memory at context {parameters.ContextSize}, " +
                        $"retrying with {smaller.ContextSize}.");
            onParametersResolved?.Invoke(smaller);
            await backend.RunChatAsync(request with { Parameters = smaller }, onLoading, onLoadOver, token)
                .ConfigureAwait(false);
        }
    }

    /// <param name="model">模型</param>
    /// <param name="fresh">显存读数过期就当场重取（会阻塞零点几秒），加载前确认用；界面上随手显示的不必</param>
    public RuntimeLoadRisk AnalyzeChatLoadRisk(ILlmModel model, bool fresh = false)
    {
        ModelRuntimeSettingConfig settings = ModelRuntimeSettingConfig.Current.ForModel(model.ModelName);
        IModelRuntimeBackend? backend = _registry.FindChatBackend(model, GetPreferredChatBackendId(model));
        if (backend == null || model is RemoteModelInfo) return RuntimeLoadRisk.Low;

        ModelMetadata metadata = ModelMetadataService.Read(model);
        RuntimeParameterPolicy policy = backend.CreateParameterPolicy(settings);
        RuntimeResolvedParameters parameters = RuntimeParameterResolver.Resolve(settings, metadata, policy);
        return RuntimeLoadRiskEvaluator.Evaluate(model, metadata, parameters, policy,
            RuntimeDeviceInfoProvider.Capture(waitForGpu: fresh));
    }

    // 本地模型按用户选的引擎优先，没选或那个引擎没注册就落到第一个能跑的
    private static string? GetPreferredChatBackendId(ILlmModel model)
    {
        return model is RemoteModelInfo ? null : ModelRuntimeSettingConfig.Current.LocalEngineId;
    }

    public async Task<IEmbeddingSession> CreateEmbeddingSessionAsync(
        EmbeddingModelSettingConfig settings,
        string modelPath,
        CancellationToken cancellationToken)
    {
        string? preferredId = EmbeddingModelResolver.IsRemote(settings)
            ? null
            : ModelRuntimeSettingConfig.Current.LocalEngineId;
        IModelRuntimeBackend? backend = _registry.FindEmbeddingBackend(settings, preferredId);
        if (backend == null)
            throw new EmbeddingRuntimeException($"No embedding backend can handle backend '{settings.Backend}'.");

        ModelMetadata metadata = File.Exists(modelPath)
            ? ModelMetadataService.FromGGufMetadata(GGufMetadataReader.TryRead(modelPath))
            : ModelMetadata.Empty;
        RuntimeResolvedParameters parameters = ResolveEmbeddingParameters(settings, metadata);
        EmbeddingRuntimeRequest request = new(settings, modelPath, metadata, parameters);
        return await backend.CreateEmbeddingSessionAsync(request, cancellationToken).ConfigureAwait(false);
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
            settings.GpuLayers == 0 ? RuntimeDeviceMode.Cpu : RuntimeDeviceMode.Auto,
            settings.GpuLayers != 0,
            false);
        return RuntimeParameterResolver.Resolve(normalized, metadata, policy);
    }
}
