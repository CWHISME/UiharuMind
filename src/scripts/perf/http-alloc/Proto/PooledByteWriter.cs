using System.Buffers;

namespace HttpAllocBench.Proto;

/// <summary>
/// P2 原型：底层数组从共享池租的 IBufferWriter，可清空复用、可回卷到某个位置。
/// 只给用完即还的临时缓冲用（请求体拷贝、改写结果、日志正文）；要活过本次调用的不能用它
/// </summary>
internal sealed class PooledByteWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _written;

    public PooledByteWriter(int initialSize)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(initialSize);
    }

    public int WrittenCount => _written;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public void Advance(int count) => _written += count;

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(_written);
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        Ensure(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_written));
        _written += bytes.Length;
    }

    public void Rewind(int mark) => _written = mark;

    public void Clear() => _written = 0;

    public void Dispose()
    {
        if (_buffer.Length == 0) return;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }

    private void Ensure(int sizeHint)
    {
        if (sizeHint < 1) sizeHint = 1;
        if (_buffer.Length - _written >= sizeHint) return;
        byte[] bigger = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _written + sizeHint));
        _buffer.AsSpan(0, _written).CopyTo(bigger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = bigger;
    }
}
