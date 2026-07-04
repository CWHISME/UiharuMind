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

public enum RuntimeCapability
{
    Chat,
    Embedding
}

public sealed record RuntimeResolvedParameters(
    int ContextSize,
    int BatchSize,
    int UBatchSize,
    int GpuLayers,
    int Threads,
    bool FlashAttention,
    bool WasAdjusted,
    string AdjustmentReason,
    RuntimeParameterRequest Request = default,
    IReadOnlyList<string>? Warnings = null)
{
    public IReadOnlyList<string> Warnings { get; init; } = Warnings ?? [];
}

public readonly record struct RuntimeParameterRequest(
    int ContextSize,
    int BatchSize,
    int UBatchSize,
    int GpuLayers,
    int Threads,
    bool IsContextAuto,
    bool IsBatchAuto,
    bool IsUBatchAuto,
    bool IsThreadsAuto);

public enum RuntimeLoadRiskLevel
{
    Low,
    Warning,
    Danger,
    Unknown
}

public sealed record RuntimeDeviceInfo(
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    long GpuTotalMemoryBytes,
    long GpuAvailableMemoryBytes,
    double ProcessCpuUsagePercent,
    string CpuName,
    string GpuName,
    string GpuMemoryNote,
    DateTimeOffset CapturedAt)
{
    public bool HasMemoryInfo => TotalMemoryBytes > 0 && AvailableMemoryBytes > 0;
    public bool HasGpuMemoryInfo => GpuTotalMemoryBytes > 0 || GpuAvailableMemoryBytes > 0;
}

public sealed record RuntimeLoadRisk(
    RuntimeLoadRiskLevel Level,
    long EstimatedTotalBytes,
    long EstimatedKvCacheBytes,
    string Reason,
    IReadOnlyList<string> Warnings)
{
    public bool RequiresConfirmation => Level == RuntimeLoadRiskLevel.Danger ||
                                        Level == RuntimeLoadRiskLevel.Unknown && Warnings.Count > 0;

    public static RuntimeLoadRisk Low { get; } = new(
        RuntimeLoadRiskLevel.Low,
        0,
        0,
        "",
        []);
}

public sealed record ModelRuntimeRequest(
    ILlmModel Model,
    ModelRuntimeSettingConfig Settings,
    ModelMetadata Metadata,
    RuntimeResolvedParameters Parameters,
    RuntimeLoadRisk LoadRisk = null!)
{
    public RuntimeLoadRisk LoadRisk { get; init; } = LoadRisk ?? RuntimeLoadRisk.Low;
}

public sealed record EmbeddingRuntimeRequest(
    EmbeddingModelSettingConfig Settings,
    string ModelPath,
    ModelMetadata Metadata,
    RuntimeResolvedParameters Parameters);
