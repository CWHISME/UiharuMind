/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Runtime;

public interface IModelRuntimeBackend
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlySet<RuntimeCapability> Capabilities { get; }

    /// <summary>
    /// 跑本地模型文件的引擎（可在「本地引擎」里选）；远程接口为 false
    /// </summary>
    bool IsLocal { get; }

    bool CanHandleChat(ILlmModel model);
    bool CanHandleEmbedding(EmbeddingModelSettingConfig settings);

    /// <summary>
    /// 按运行设置给出参数解析策略（设备、能否上 GPU）
    /// </summary>
    /// <param name="settings">运行设置</param>
    /// <returns>参数解析策略</returns>
    RuntimeParameterPolicy CreateParameterPolicy(ModelRuntimeSettingConfig settings);

    /// <summary>
    /// 引擎专属的模型来源（如远程服务商）。本地 GGUF 不归任何引擎，由 <see cref="Models.LocalModelScanner"/> 统一发现
    /// </summary>
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
    public IReadOnlyList<IModelRuntimeBackend> LocalEngines => _backends.Where(x => x.IsLocal).ToList();

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

    public IModelRuntimeBackend? FindEmbeddingBackend(EmbeddingModelSettingConfig settings, string? preferredBackendId = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredBackendId))
        {
            IModelRuntimeBackend? preferred = _backends.FirstOrDefault(x => x.Id == preferredBackendId);
            if (preferred?.CanHandleEmbedding(settings) == true) return preferred;
        }

        return _backends.FirstOrDefault(x => x.CanHandleEmbedding(settings));
    }
}
