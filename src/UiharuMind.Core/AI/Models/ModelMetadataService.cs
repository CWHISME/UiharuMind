/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Models;

public static class ModelMetadataService
{
    public static ModelMetadata Read(ILlmModel model)
    {
        if (model is GGufModelInfo gguf)
            return FromGGufModelInfo(gguf);

        if (File.Exists(model.ModelPath))
            return FromGGufMetadata(GGufMetadataReader.TryRead(model.ModelPath));

        return ModelMetadata.Empty;
    }

    public static ModelMetadata FromGGufModelInfo(GGufModelInfo model)
    {
        return new ModelMetadata
        {
            Architecture = model.Architecture,
            DisplayName = model.DisplayName,
            SizeLabel = model.SizeLabel,
            ContextLength = model.ContextLength,
            EmbeddingLength = model.EmbeddingLength,
            LayerCount = model.LayerCount,
            HeadCount = model.AttentionHeadCount,
            KvHeadCount = model.AttentionHeadCountKv,
            KeyLength = model.AttentionKeyLength,
            ValueLength = model.AttentionValueLength,
            FileSizeBytes = model.FileSizeBytes
        };
    }

    public static ModelMetadata FromGGufMetadata(GGufMetadataInfo? metadata)
    {
        if (metadata == null) return ModelMetadata.Empty;

        return new ModelMetadata
        {
            Architecture = metadata.Architecture,
            DisplayName = metadata.DisplayName,
            SizeLabel = metadata.SizeLabel,
            ContextLength = metadata.ContextLength,
            EmbeddingLength = metadata.EmbeddingLength,
            LayerCount = metadata.LayerCount,
            HeadCount = metadata.AttentionHeadCount,
            KvHeadCount = metadata.AttentionHeadCountKv,
            KeyLength = metadata.AttentionKeyLength,
            ValueLength = metadata.AttentionValueLength,
            FileSizeBytes = metadata.FileSizeBytes
        };
    }
}
