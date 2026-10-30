using System.ClientModel;

namespace UiharuMind.Core.AI.Net.Transport;

/// <summary>
/// 切成 64KB 段的请求体（SDK 管道版）：每段小于 85000 字节，不进大对象堆。
/// 切段逻辑在 <see cref="SegmentedBuffer"/>，这里只负责包成 <see cref="BinaryContent"/>。
///
/// 内容只读，重试再发一遍同一份即可，<see cref="WriteToAsync"/> 可重入。
/// </summary>
internal sealed class SegmentedBinaryContent : BinaryContent
{
    private readonly byte[][] _segments;
    private readonly int _length;

    /// <summary>
    /// 拷一份正文
    /// </summary>
    /// <param name="data">正文，调用返回后即可复用</param>
    public SegmentedBinaryContent(ReadOnlySpan<byte> data)
    {
        _length = data.Length;
        _segments = SegmentedBuffer.Split(data);
    }

    /// <inheritdoc />
    public override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }

    /// <inheritdoc />
    public override void WriteTo(Stream stream, CancellationToken cancellation = default)
    {
        foreach (byte[] segment in _segments)
        {
            cancellation.ThrowIfCancellationRequested();
            stream.Write(segment);
        }
    }

    /// <inheritdoc />
    public override Task WriteToAsync(Stream stream, CancellationToken cancellation = default) =>
        SegmentedBuffer.WriteToAsync(_segments, stream, cancellation);

    /// <inheritdoc />
    public override void Dispose()
    {
    }
}
