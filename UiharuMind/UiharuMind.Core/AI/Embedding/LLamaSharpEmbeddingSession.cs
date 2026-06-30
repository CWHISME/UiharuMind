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

using LLama;
using LLama.Common;
using LLama.Exceptions;
using LLama.Native;

namespace UiharuMind.Core.AI.Embedding;

public sealed class LLamaSharpEmbeddingSession : IEmbeddingSession
{
    private readonly SemaphoreSlim _generationLock = new(1, 1);
    private readonly LLamaWeights _weights;
    private readonly LLamaEmbedder _embedder;
    private bool _disposed;

    private LLamaSharpEmbeddingSession(
        string modelPath, LLamaWeights weights, LLamaEmbedder embedder)
    {
        ModelPath = modelPath;
        _weights = weights;
        _embedder = embedder;
        Dimensions = embedder.EmbeddingSize;
    }

    public string BackendName => "LLamaSharp";
    public string ModelPath { get; }
    public int Dimensions { get; }
    public bool IsRunning => !_disposed;
    public string LastError { get; private set; } = "";

    public static async Task<LLamaSharpEmbeddingSession> CreateAsync(
        string modelPath,
        int contextSize,
        int batchSize,
        int uBatchSize,
        int gpuLayers,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(modelPath)) throw new FileNotFoundException("Embedding model file not found.", modelPath);

        var modelParams = new ModelParams(modelPath)
        {
            ContextSize = (uint)Math.Max(1, contextSize),
            BatchSize = (uint)Math.Max(1, batchSize),
            UBatchSize = (uint)Math.Max(1, uBatchSize),
            GpuLayerCount = gpuLayers,
            Embeddings = true,
            PoolingType = LLamaPoolingType.Mean
        };

        try
        {
            LLamaWeights weights =
                await LLamaWeights.LoadFromFileAsync(modelParams, cancellationToken, null).ConfigureAwait(false);
            var embedder = new LLamaEmbedder(weights, modelParams, null!);
            return new LLamaSharpEmbeddingSession(modelPath, weights, embedder);
        }
        catch (Exception e)
        {
            throw new EmbeddingRuntimeException($"Failed to load LLamaSharp embedding model: {e.Message}", e);
        }
    }

    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text)) return ReadOnlyMemory<float>.Empty;

        await _generationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<float[]> embeddings =
                await _embedder.GetEmbeddings(text, cancellationToken).ConfigureAwait(false);
            if (embeddings.Count == 0)
                throw new EmbeddingRuntimeException("Embedding model returned no vector.");

            float[] vector = embeddings.Count == 1
                ? embeddings[0]
                : MeanPool(embeddings);
            EmbeddingVectorUtils.NormalizeInPlace(vector);
            LastError = "";
            return vector;
        }
        catch (ContextOverflowException e)
        {
            LastError = e.Message;
            throw new EmbeddingInputTooLargeException(e.Message, e);
        }
        catch (RuntimeError e) when (IsInputTooLargeError(e.Message))
        {
            LastError = e.Message;
            throw new EmbeddingInputTooLargeException(e.Message, e);
        }
        catch (EmbeddingInputTooLargeException)
        {
            throw;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            throw new EmbeddingRuntimeException($"LLamaSharp embedding request failed: {e.Message}", e);
        }
        finally
        {
            _generationLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _embedder.Dispose();
        _weights.Dispose();
        _generationLock.Dispose();
    }

    private static float[] MeanPool(IReadOnlyList<float[]> embeddings)
    {
        int dimension = embeddings[0].Length;
        float[] pooled = new float[dimension];
        foreach (float[] embedding in embeddings)
        {
            for (int i = 0; i < dimension; i++)
                pooled[i] += embedding[i];
        }

        float divisor = embeddings.Count;
        for (int i = 0; i < dimension; i++)
            pooled[i] /= divisor;
        return pooled;
    }

    private static bool IsInputTooLargeError(string message)
    {
        return message.Contains("input is too large", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("increase the physical batch size", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("context", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("overflow", StringComparison.OrdinalIgnoreCase);
    }
}
