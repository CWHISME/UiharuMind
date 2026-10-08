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
    Func<CancellationToken, Task<VersionInfo?>> currentVersion) : IModelRuntimeBackend
{
    public const string BackendId = "LLamaCpp";

    public string Id => BackendId;
    public string DisplayName => "llama.cpp";
    public IReadOnlySet<RuntimeCapability> Capabilities { get; } =
        new HashSet<RuntimeCapability>
            { RuntimeCapability.Chat, RuntimeCapability.Embedding, RuntimeCapability.ToolCalling };

    public bool IsLocal => true;

    public bool CanHandleChat(ILlmModel model)
    {
        // 音频模型归音频引擎（ADR 0071）；读不出头的旧模型照旧当对话处理
        return model is GGufModelInfo info && info.Kind?.IsAudio() != true;
    }

    public bool CanHandleEmbedding(EmbeddingModelSettingConfig settings)
    {
        return !EmbeddingModelResolver.IsRemote(settings);
    }

    public RuntimeParameterPolicy CreateParameterPolicy(ModelRuntimeSettingConfig settings)
    {
        return new RuntimeParameterPolicy(
            settings.GpuLayers == 0 ? RuntimeDeviceMode.Cpu : RuntimeDeviceMode.Auto,
            settings.GpuLayers != 0,
            false);
    }

    public Task<IReadOnlyDictionary<string, ILlmModel>> DiscoverModelsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<string, ILlmModel>>(new Dictionary<string, ILlmModel>());
    }

    public async Task RunChatAsync(
        ModelRuntimeRequest request,
        Action<float>? onLoading,
        Action<IChatClient>? onLoadOver,
        CancellationToken cancellationToken)
    {
        VersionInfo version = await currentVersion(cancellationToken).ConfigureAwait(false) ??
                              throw new LocalEngineNotReadyException();

        await server.Run(version, request.Model, request.Parameters, onLoading, onLoadOver, token: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IEmbeddingSession> CreateEmbeddingSessionAsync(
        EmbeddingRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        VersionInfo version = await currentVersion(cancellationToken).ConfigureAwait(false) ??
                              throw new LocalEngineNotReadyException();

        return await LLamaCppEmbeddingSession.StartAsync(
            version,
            LLamaCppSettingConfig.Current,
            request.ModelPath,
            request.Parameters,
            cancellationToken).ConfigureAwait(false);
    }

}
