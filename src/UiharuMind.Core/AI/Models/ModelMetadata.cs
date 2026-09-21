/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

namespace UiharuMind.Core.AI.Models;

public sealed class ModelMetadata
{
    public string Architecture { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string SizeLabel { get; init; } = "";
    public int ContextLength { get; init; }
    public int EmbeddingLength { get; init; }
    public int LayerCount { get; init; }
    public ulong FileSizeBytes { get; init; }

    public static ModelMetadata Empty { get; } = new();
}
