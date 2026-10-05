using System.Buffers;

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 底层数组从共享池租的 <see cref="IBufferWriter{T}"/>，可回卷到之前的某个位置。
/// 只给用完即还的临时缓冲用（请求体拷贝、改写结果、日志正文）；要活过本次调用的数据不能放在这里
/// </summary>
internal sealed class PooledByteWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _written;

    /// <summary>
    /// 按预估大小租第一块缓冲
    /// </summary>
    /// <param name="initialSize">预估大小，不够时翻倍换租</param>
    public PooledByteWriter(int initialSize)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialSize, 256));
    }

    /// <summary>已写入的字节数</summary>
    public int WrittenCount => _written;

    /// <summary>已写入的内容</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    /// <summary>已写入的内容</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    /// <inheritdoc />
    public void Advance(int count) => _written += count;

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(_written);
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(_written);
    }

    /// <summary>
    /// 追加一段字节
    /// </summary>
    /// <param name="bytes">要追加的内容</param>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        Ensure(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_written));
        _written += bytes.Length;
    }

    /// <summary>
    /// 回卷到之前记下的位置，之后的内容作废
    /// </summary>
    /// <param name="mark">先前取的 <see cref="WrittenCount"/></param>
    public void Rewind(int mark) => _written = mark;

    /// <summary>
    /// 只写的流视图：往流里写就是往这里追加，给只认 <see cref="Stream"/> 的序列化方用
    /// </summary>
    /// <returns>不持有缓冲的流，释放它不影响本对象</returns>
    public Stream AsStream() => new WriteStream(this);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_buffer.Length == 0) return;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
        _written = 0;
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

    private sealed class WriteStream(PooledByteWriter owner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => owner.WrittenCount;

        public override long Position
        {
            get => owner.WrittenCount;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => owner.Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => owner.Write(buffer);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
