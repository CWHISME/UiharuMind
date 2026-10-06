/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using System.Globalization;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Models;

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
    public ELocalModelKind Kind { get; init; }
    public IReadOnlyDictionary<string, string> RawMetadata { get; init; } = new Dictionary<string, string>();
}

public static class GGufMetadataReader
{
    public static GGufMetadataInfo? TryRead(string modelPath)
    {
        try
        {
            if (!File.Exists(modelPath)) return null;

            GGufHeader header = GGufHeaderReader.Read(modelPath);
            string architecture = header.GetString("general.architecture");
            string prefix = string.IsNullOrWhiteSpace(architecture) ? "" : architecture + ".";

            return new GGufMetadataInfo
            {
                Architecture = architecture,
                DisplayName = header.GetString("general.name"),
                SizeLabel = header.GetString("general.size_label"),
                Quantization = header.GetInteger("general.file_type")?.ToString(CultureInfo.InvariantCulture) ?? "",
                ContextLength = GetInt(header, prefix + "context_length"),
                EmbeddingLength = GetInt(header, prefix + "embedding_length"),
                LayerCount = GetInt(header, prefix + "block_count"),
                AttentionHeadCount = GetInt(header, prefix + "attention.head_count"),
                AttentionHeadCountKv = GetInt(header, prefix + "attention.head_count_kv"),
                ParameterCount = header.ParameterCount,
                FileSizeBytes = (ulong)new FileInfo(modelPath).Length,
                Kind = LocalModelKindClassifier.Classify(header),
                RawMetadata = header.ToStringDictionary()
            };
        }
        catch (Exception e)
        {
            Log.Warning($"Read GGUF metadata failed: {modelPath}, {e.Message}");
            return null;
        }
    }

    private static int GetInt(GGufHeader header, string key)
    {
        long? value = header.GetInteger(key);
        return value is > 0 and <= int.MaxValue ? (int)value.Value : 0;
    }
}
