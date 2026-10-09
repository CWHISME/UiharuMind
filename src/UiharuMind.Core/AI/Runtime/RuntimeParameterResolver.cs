/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using UiharuMind.Core.Configs;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Runtime;

public enum RuntimeDeviceMode
{
    Cpu,
    Gpu,
    Auto
}

/// <param name="KvBytesK">KV 缓存 K 每个元素占几字节（f16 为 2，q8_0 约 1）</param>
/// <param name="KvBytesV">KV 缓存 V 每个元素占几字节</param>
public sealed record RuntimeParameterPolicy(
    RuntimeDeviceMode DeviceMode,
    bool CanUseGpu,
    bool HasReliableDeviceMemory,
    double KvBytesK = 2,
    double KvBytesV = 2);

public static class RuntimeParameterResolver
{
    private const int FallbackContextSize = 4096;
    private const int DefaultBatchSize = 2048; //与 llama-server 默认一致
    private const int DefaultUBatchSize = 512;
    private const int MinRetryContextSize = 2048; //内存不够重试时上下文最小减到这里

    public static RuntimeResolvedParameters Resolve(
        ModelRuntimeSettingConfig settings,
        ModelMetadata metadata,
        RuntimeParameterPolicy policy)
    {
        bool contextAuto = settings.ContextSize <= 0;
        bool batchAuto = settings.BatchSize <= 0;
        bool uBatchAuto = settings.UBatchSize <= 0;
        bool threadsAuto = settings.Threads <= 0;

        int modelContext = metadata.ContextLength > 0 ? metadata.ContextLength : FallbackContextSize;
        int contextSize = contextAuto ? ResolveAutoContext(modelContext, policy) : Math.Max(1, settings.ContextSize);
        int batchSize = batchAuto ? ResolveAutoBatch(contextSize, policy) : Math.Max(1, settings.BatchSize);
        int uBatchSize = uBatchAuto ? ResolveAutoUBatch(batchSize, policy) : Math.Max(1, settings.UBatchSize);
        int threads = Math.Max(0, settings.Threads);

        List<string> warnings = [];
        if (!contextAuto && metadata.ContextLength > 0 && contextSize > metadata.ContextLength)
            warnings.Add("Context size is larger than the model metadata context length.");
        if (!batchAuto && batchSize > contextSize)
            warnings.Add("Batch size is larger than context size.");
        if (!uBatchAuto && uBatchSize > batchSize)
            warnings.Add("Physical batch size is larger than logical batch size.");

        RuntimeParameterRequest request = new(
            settings.ContextSize,
            settings.BatchSize,
            settings.UBatchSize,
            settings.GpuLayers,
            settings.Threads,
            contextAuto,
            batchAuto,
            uBatchAuto,
            threadsAuto);

        bool adjusted = contextAuto || batchAuto || uBatchAuto || threadsAuto;
        string reason = adjusted
            ? $"context={contextSize}, batch={batchSize}, ubatch={uBatchSize}, threads={(threadsAuto ? "auto" : threads)}"
            : "";

        return new RuntimeResolvedParameters(
            contextSize,
            batchSize,
            uBatchSize,
            settings.GpuLayers,
            threads,
            settings.FlashAttention,
            adjusted,
            reason,
            request,
            warnings) { KvCacheType = settings.KvCacheType };
    }

    /// <summary>
    /// 内存不够起不来时上下文减半再试；KV 缓存与上下文成正比，减半就省一半
    /// </summary>
    /// <param name="parameters">起不来的那组参数</param>
    /// <returns>减半后的参数；已经不能再小为 null</returns>
    public static RuntimeResolvedParameters? HalveContext(RuntimeResolvedParameters parameters)
    {
        int half = parameters.ContextSize / 2 / 256 * 256;
        if (half < MinRetryContextSize) return null;
        return parameters with
        {
            ContextSize = half,
            BatchSize = Math.Min(parameters.BatchSize, half),
            UBatchSize = Math.Min(parameters.UBatchSize, half),
            WasAdjusted = true,
            AdjustmentReason = $"Context halved from {parameters.ContextSize} after running out of memory."
        };
    }

    private static int ResolveAutoContext(int modelContext, RuntimeParameterPolicy policy)
    {
        // Auto 只给保守推荐，不再作为用户显式参数的硬上限。
        int divisor = policy.DeviceMode == RuntimeDeviceMode.Cpu ? 2 : 4;
        return Math.Max(512, Math.Min(modelContext, Math.Max(FallbackContextSize, modelContext / divisor)));
    }

    private static int ResolveAutoBatch(int contextSize, RuntimeParameterPolicy policy)
    {
        return Math.Clamp(DefaultBatchSize, 1, Math.Max(1, contextSize));
    }

    private static int ResolveAutoUBatch(int batchSize, RuntimeParameterPolicy policy)
    {
        return Math.Clamp(DefaultUBatchSize, 1, Math.Max(1, batchSize));
    }
}
