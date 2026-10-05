using System.ClientModel;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 切成 64KB 段的请求体：每段小于 85000 字节，不进大对象堆，几十万字的请求体也只是一串小数组。
///
/// 段归 GC 管、<b>不从池里租</b>：HTTP/2 可能在请求体发完之前就收到响应，那时还池会把别处的数据发出去。
/// 内容只读，重试再发一遍同一份即可，<see cref="WriteToAsync"/> 可重入。
/// </summary>
internal sealed class SegmentedBinaryContent : BinaryContent
{
    private const int SegmentSize = 64 * 1024;

    private readonly byte[][] _segments;
    private readonly int _length;

    /// <summary>
    /// 拷一份正文
    /// </summary>
    /// <param name="data">正文，调用返回后即可复用</param>
    public SegmentedBinaryContent(ReadOnlySpan<byte> data)
    {
        _length = data.Length;
        _segments = new byte[(data.Length + SegmentSize - 1) / SegmentSize][];
        for (int i = 0; i < _segments.Length; i++)
        {
            int size = Math.Min(SegmentSize, data.Length);
            _segments[i] = GC.AllocateUninitializedArray<byte>(size);
            data[..size].CopyTo(_segments[i]);
            data = data[size..];
        }
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
    public override async Task WriteToAsync(Stream stream, CancellationToken cancellation = default)
    {
        foreach (byte[] segment in _segments) await stream.WriteAsync(segment, cancellation).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
    }
}
