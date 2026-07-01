/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using System.Globalization;
using LLama;
using LLama.Common;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.LocalAI.GGuf;

public sealed class GGufMetadataInfo
{
    public string Architecture { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string SizeLabel { get; init; } = "";
    public string Quantization { get; init; } = "";
    public int ContextLength { get; init; }
    public int EmbeddingLength { get; init; }
    public int LayerCount { get; init; }
    public int AttentionHeadCount { get; init; }
    public int AttentionHeadCountKv { get; init; }
    public ulong ParameterCount { get; init; }
    public ulong FileSizeBytes { get; init; }
    public IReadOnlyDictionary<string, string> RawMetadata { get; init; } = new Dictionary<string, string>();
}

public static class GGufMetadataReader
{
    public static GGufMetadataInfo? TryRead(string modelPath)
    {
        try
        {
            if (!File.Exists(modelPath)) return null;

            // VocabOnly 只读取词表和元信息，避免为了展示模型信息就分配完整模型张量与上下文。
            using LLamaWeights weights = LLamaWeights.LoadFromFile(new ModelParams(modelPath)
            {
                VocabOnly = true,
                GpuLayerCount = 0,
                UseMemorymap = true
            });

            IReadOnlyDictionary<string, string> metadata = weights.Metadata;
            string architecture = GetString(metadata, "general.architecture");
            string prefix = string.IsNullOrWhiteSpace(architecture) ? "" : architecture + ".";

            return new GGufMetadataInfo
            {
                Architecture = architecture,
                DisplayName = GetString(metadata, "general.name"),
                SizeLabel = GetString(metadata, "general.size_label"),
                Quantization = GetString(metadata, "general.file_type"),
                ContextLength = GetInt(metadata, prefix + "context_length", weights.ContextSize),
                EmbeddingLength = GetInt(metadata, prefix + "embedding_length", weights.EmbeddingSize),
                LayerCount = GetInt(metadata, prefix + "block_count"),
                AttentionHeadCount = GetInt(metadata, prefix + "attention.head_count"),
                AttentionHeadCountKv = GetInt(metadata, prefix + "attention.head_count_kv"),
                ParameterCount = weights.ParameterCount,
                FileSizeBytes = weights.SizeInBytes,
                RawMetadata = new Dictionary<string, string>(metadata)
            };
        }
        catch (Exception e)
        {
            Log.Warning($"Read GGUF metadata failed: {modelPath}, {e.Message}");
            return null;
        }
    }

    private static string GetString(IReadOnlyDictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key, out string? value) ? value : "";
    }

    private static int GetInt(IReadOnlyDictionary<string, string> metadata, string key, int fallback = 0)
    {
        if (!metadata.TryGetValue(key, out string? value)) return fallback;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : fallback;
    }
}
