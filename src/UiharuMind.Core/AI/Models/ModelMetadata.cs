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

    /// <summary>
    /// 注意力头数；0 为不知道
    /// </summary>
    public int HeadCount { get; init; }

    /// <summary>
    /// 存 K/V 的头数（GQA 远少于 <see cref="HeadCount"/>）；0 为不知道
    /// </summary>
    public int KvHeadCount { get; init; }

    /// <summary>
    /// 每个头 K 的维度；0 为按嵌入维度 / 头数算
    /// </summary>
    public int KeyLength { get; init; }

    /// <summary>
    /// 每个头 V 的维度；0 为按嵌入维度 / 头数算
    /// </summary>
    public int ValueLength { get; init; }
    public ulong FileSizeBytes { get; init; }

    public static ModelMetadata Empty { get; } = new();
}
