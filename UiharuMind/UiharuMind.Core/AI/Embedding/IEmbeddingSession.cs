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

    /// <summary>
    /// 一次生成多条文本的向量。
    ///
    /// 默认实现逐条调用,本地后端不必覆写——它们没有网络往返,批量省不下什么。
    /// 远程后端应当覆写:索引一份长文档动辄上千块,逐条发就是上千次串行 HTTP 往返。
    ///
    /// 契约:返回的向量顺序与 <paramref name="texts"/> 一致且数量相同。
    /// 整批因输入过长被拒时抛 <see cref="EmbeddingInputTooLargeException"/>——
    /// 批量请求无法指出是哪一条,调用方需要退成逐条才能定位。
    /// </summary>
    /// <param name="texts">待嵌入的文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>与输入一一对应的向量</returns>
    async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        ReadOnlyMemory<float>[] vectors = new ReadOnlyMemory<float>[texts.Count];
        for (int index = 0; index < texts.Count; index++)
        {
            vectors[index] = await GenerateEmbeddingAsync(texts[index], cancellationToken).ConfigureAwait(false);
        }

        return vectors;
    }
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
