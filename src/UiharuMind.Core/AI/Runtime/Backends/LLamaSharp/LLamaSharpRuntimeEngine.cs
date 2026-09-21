/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using LLama;
using LLama.Abstractions;
using LLama.Common;
using LLama.Native;
using LLama.Transformers;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Runtime.Backends;

public sealed class LLamaSharpRuntimeEngine
{
    public async Task Run(
        ILlmModel model,
        Action<float>? onLoading = null,
        Action<IChatClient>? onLoadOver = null,
        CancellationToken token = default)
    {
        ModelRuntimeSettingConfig config = ModelRuntimeSettingConfig.Current;
        ModelMetadata metadata = ModelMetadataService.Read(model);
        RuntimeResolvedParameters parameters = RuntimeParameterResolver.Resolve(
            config, metadata, CreatePolicy(config));
        await Run(model, parameters, config, onLoading, onLoadOver, token).ConfigureAwait(false);
    }

    public async Task Run(
        ILlmModel model,
        RuntimeResolvedParameters parameters,
        ModelRuntimeSettingConfig config,
        Action<float>? onLoading = null,
        Action<IChatClient>? onLoadOver = null,
        CancellationToken token = default)
    {
        ConfigureNativeBackend(config);
        if (parameters.WasAdjusted)
            Log.Warning($"LLamaSharp runtime parameters adjusted: {parameters.AdjustmentReason}");

        ModelParams modelParams = new(model.ModelPath)
        {
            ContextSize = (uint)parameters.ContextSize,
            GpuLayerCount = parameters.GpuLayers,
            BatchSize = (uint)parameters.BatchSize,
            UBatchSize = (uint)parameters.UBatchSize,
            Threads = parameters.Threads <= 0 ? null : parameters.Threads,
            FlashAttention = parameters.FlashAttention
        };

        using LLamaWeights weights = await LLamaWeights
            .LoadFromFileAsync(modelParams, token, new Progress<float>(x => onLoading?.Invoke(x)))
            .ConfigureAwait(false);
        StatelessExecutor executor = new(weights, modelParams);

        // 使用 GGUF 自带 chat template，避免把 "User:" 这类纯文本角色标签喂给模型续写。
        IHistoryTransform historyTransform = new PromptTemplateTransformer(weights, true);
        IChatClient chatClient = new LLamaSharpChatClient(executor, historyTransform, weights, parameters.ContextSize);

        onLoading?.Invoke(1);
        onLoadOver?.Invoke(chatClient);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 模型运行生命周期由 ModelRunningData 的取消 token 控制；取消时正常释放上下文。
        }
    }

    public static RuntimeParameterPolicy CreatePolicy(ModelRuntimeSettingConfig config)
    {
        RuntimeDeviceMode mode = config.LLamaSharpBackendMode == ModelRuntimeSettingConfig.LLamaSharpBackendCpu &&
                                 config.GpuLayers <= 0
            ? RuntimeDeviceMode.Cpu
            : RuntimeDeviceMode.Auto;
        return new RuntimeParameterPolicy(
            mode,
            config.GpuLayers > 0 || config.LLamaSharpBackendMode != ModelRuntimeSettingConfig.LLamaSharpBackendCpu,
            false);
    }

    private static void ConfigureNativeBackend(ModelRuntimeSettingConfig config)
    {
        try
        {
            switch (config.LLamaSharpBackendMode)
            {
                case ModelRuntimeSettingConfig.LLamaSharpBackendCpu:
                    NativeLibraryConfig.All.WithCuda(false).WithVulkan(false).WithAutoFallback(true);
                    break;
                case ModelRuntimeSettingConfig.LLamaSharpBackendGpu:
                    NativeLibraryConfig.All.WithAutoFallback(true);
                    break;
                default:
                    NativeLibraryConfig.All.WithAutoFallback(true);
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"LLamaSharp backend configuration failed: {e.Message}");
        }
    }
}
