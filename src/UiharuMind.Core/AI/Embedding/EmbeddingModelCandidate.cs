/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

namespace UiharuMind.Core.AI.Embedding;

public enum EmbeddingModelCandidateSource
{
    Application,
    BuiltIn
}

public sealed class EmbeddingModelCandidate
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public EmbeddingModelCandidateSource Source { get; init; }
    public long SizeBytes { get; init; }
}
