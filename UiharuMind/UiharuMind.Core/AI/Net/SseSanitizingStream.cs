using System.Text;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 按行转发 SSE 响应，逐行交给 <see cref="OpenAiCompatibleResponseFixer"/> 修正后再吐给下游。
/// 只做行缓冲，不整体缓冲，流式输出的实时性不受影响。
/// </summary>
internal sealed class SseSanitizingStream : Stream
{
    private readonly Stream _inner;
    private readonly StreamReader _reader;
    private byte[] _pending = []; //当前行修正后的字节
    private int _offset;

    public SseSanitizingStream(Stream inner)
    {
        _inner = inner;
        _reader = new StreamReader(inner, Encoding.UTF8, false, 1024, true);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        while (_offset >= _pending.Length)
        {
            var line = _reader.ReadLine();
            if (line == null) return 0;
            SetPending(line);
        }

        return CopyPending(buffer.AsSpan(offset, count));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (_offset >= _pending.Length)
        {
            var line = await _reader.ReadLineAsync(cancellationToken);
            if (line == null) return 0;
            SetPending(line);
        }

        return CopyPending(buffer.Span);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void SetPending(string line)
    {
        _pending = Encoding.UTF8.GetBytes(OpenAiCompatibleResponseFixer.FixEventStreamLine(line) + "\n");
        _offset = 0;
    }

    private int CopyPending(Span<byte> destination)
    {
        var length = Math.Min(destination.Length, _pending.Length - _offset);
        _pending.AsSpan(_offset, length).CopyTo(destination);
        _offset += length;
        return length;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reader.Dispose();
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
