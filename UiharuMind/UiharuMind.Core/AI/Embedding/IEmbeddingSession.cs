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

public interface IEmbeddingSession : IDisposable
{
    string BackendName { get; }
    string ModelPath { get; }
    int Dimensions { get; }
    bool IsRunning { get; }
    string LastError { get; }

    Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text, CancellationToken cancellationToken = default);
}

public sealed class EmbeddingRuntimeException : Exception
{
    public EmbeddingRuntimeException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class EmbeddingInputTooLargeException : Exception
{
    public EmbeddingInputTooLargeException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
