/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Runtime;

public enum RuntimeDeviceMode
{
    Cpu,
    Gpu,
    Auto
}

public sealed record RuntimeParameterPolicy(
    RuntimeDeviceMode DeviceMode,
    bool CanUseGpu,
    bool HasReliableDeviceMemory);

public static class RuntimeParameterResolver
{
    private const int FallbackContextSize = 4096;
    private const int FallbackBatchSize = 512;

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
            warnings);
    }

    private static int ResolveAutoContext(int modelContext, RuntimeParameterPolicy policy)
    {
        // Auto 只给保守推荐，不再作为用户显式参数的硬上限。
        int divisor = policy.DeviceMode == RuntimeDeviceMode.Cpu ? 2 : 4;
        return Math.Max(512, Math.Min(modelContext, Math.Max(FallbackContextSize, modelContext / divisor)));
    }

    private static int ResolveAutoBatch(int contextSize, RuntimeParameterPolicy policy)
    {
        int target = policy.DeviceMode == RuntimeDeviceMode.Cpu ? FallbackBatchSize : FallbackBatchSize / 2;
        return Math.Clamp(target, 1, Math.Max(1, contextSize));
    }

    private static int ResolveAutoUBatch(int batchSize, RuntimeParameterPolicy policy)
    {
        int target = policy.DeviceMode == RuntimeDeviceMode.Cpu ? batchSize : Math.Min(batchSize, FallbackBatchSize / 2);
        return Math.Clamp(target, 1, Math.Max(1, batchSize));
    }
}
