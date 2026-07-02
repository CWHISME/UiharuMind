/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Runtime;

public static class RuntimeLoadRiskEvaluator
{
    private const long MiB = 1024L * 1024L;

    public static RuntimeLoadRisk Evaluate(
        ILlmModel model,
        ModelMetadata metadata,
        RuntimeResolvedParameters parameters,
        RuntimeParameterPolicy policy,
        RuntimeDeviceInfo deviceInfo)
    {
        if (!File.Exists(model.ModelPath))
            return new RuntimeLoadRisk(RuntimeLoadRiskLevel.Unknown, 0, 0, "Model file is missing.", [
                "Model file is missing."
            ]);

        long modelBytes = metadata.FileSizeBytes > 0
            ? checked((long)Math.Min(metadata.FileSizeBytes, long.MaxValue))
            : new FileInfo(model.ModelPath).Length;
        long kvBytes = EstimateKvCacheBytes(metadata, parameters.ContextSize);
        long computeBytes = EstimateComputeBytes(metadata, parameters);
        long estimatedTotal = SafeAdd(modelBytes, SafeAdd(kvBytes, computeBytes));
        List<string> warnings = [];

        if (parameters.Warnings.Count > 0) warnings.AddRange(parameters.Warnings);
        if (!deviceInfo.HasMemoryInfo)
        {
            if (HasAggressiveExplicitParameters(metadata, parameters))
                warnings.Add("Device memory is unknown and explicit runtime parameters are aggressive.");
            return new RuntimeLoadRisk(
                warnings.Count > 0 ? RuntimeLoadRiskLevel.Unknown : RuntimeLoadRiskLevel.Warning,
                estimatedTotal,
                kvBytes,
                "Device memory is unknown.",
                warnings);
        }

        double totalRatio = estimatedTotal / Math.Max(1.0, deviceInfo.TotalMemoryBytes);
        double availableRatio = estimatedTotal / Math.Max(1.0, deviceInfo.AvailableMemoryBytes);
        RuntimeLoadRiskLevel level = RuntimeLoadRiskLevel.Low;

        // 使用内存占比判断风险，而不是写死上下文上限；Metal 统一内存也按系统可用内存保守提示。
        if (availableRatio >= 0.9 || totalRatio >= 0.85)
        {
            level = RuntimeLoadRiskLevel.Danger;
            warnings.Add("Estimated runtime memory is close to or above available memory.");
        }
        else if (availableRatio >= 0.65 || totalRatio >= 0.6)
        {
            level = RuntimeLoadRiskLevel.Warning;
            warnings.Add("Estimated runtime memory may create high memory pressure.");
        }

        if (HasAggressiveExplicitParameters(metadata, parameters))
        {
            if (level == RuntimeLoadRiskLevel.Low) level = RuntimeLoadRiskLevel.Warning;
            warnings.Add("Explicit context or batch settings are much larger than the Auto recommendation.");
        }

        return new RuntimeLoadRisk(
            level,
            estimatedTotal,
            kvBytes,
            BuildReason(level, estimatedTotal, deviceInfo),
            warnings.Distinct().ToArray());
    }

    private static long EstimateKvCacheBytes(ModelMetadata metadata, int contextSize)
    {
        int layers = metadata.LayerCount > 0 ? metadata.LayerCount : 32;
        int embedding = metadata.EmbeddingLength > 0 ? metadata.EmbeddingLength : 4096;
        double bytes = (double)Math.Max(1, contextSize) * layers * embedding * 2 * 2;
        return bytes >= long.MaxValue ? long.MaxValue : (long)bytes;
    }

    private static long EstimateComputeBytes(ModelMetadata metadata, RuntimeResolvedParameters parameters)
    {
        int layers = metadata.LayerCount > 0 ? metadata.LayerCount : 32;
        int embedding = metadata.EmbeddingLength > 0 ? metadata.EmbeddingLength : 4096;
        int batch = Math.Max(parameters.BatchSize, parameters.UBatchSize);
        double bytes = (double)batch * layers * embedding * 4;
        return bytes >= long.MaxValue ? long.MaxValue : (long)Math.Max(0, bytes);
    }

    private static bool HasAggressiveExplicitParameters(ModelMetadata metadata, RuntimeResolvedParameters parameters)
    {
        bool explicitContext = !parameters.Request.IsContextAuto;
        bool explicitBatch = !parameters.Request.IsBatchAuto || !parameters.Request.IsUBatchAuto;
        bool highContext = metadata.ContextLength > 0 &&
                           parameters.ContextSize >= Math.Max(1, metadata.ContextLength * 0.75);
        bool highBatch = parameters.BatchSize > Math.Max(1024, parameters.ContextSize / 4) ||
                         parameters.UBatchSize > Math.Max(512, parameters.BatchSize / 2);
        return explicitContext && highContext || explicitBatch && highBatch;
    }

    private static string BuildReason(RuntimeLoadRiskLevel level, long estimatedTotal, RuntimeDeviceInfo deviceInfo)
    {
        if (!deviceInfo.HasMemoryInfo) return "Device memory is unknown.";
        return level switch
        {
            RuntimeLoadRiskLevel.Danger => "Estimated runtime memory may exceed safe available memory.",
            RuntimeLoadRiskLevel.Warning => "Estimated runtime memory may cause high memory pressure.",
            _ => "Estimated runtime memory is within the current device budget."
        };
    }

    private static long SafeAdd(long left, long right)
    {
        if (left > long.MaxValue - right) return long.MaxValue;
        return left + right;
    }
}
