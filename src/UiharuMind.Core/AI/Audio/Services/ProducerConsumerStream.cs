using System.Collections.Concurrent;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 单生产者单消费者的内存流：生产者写入字节块，消费者边到边读。
/// 用于流式合成——第一个音频块到达就能开始播放，不用等整段收完
/// </summary>
internal sealed class ProducerConsumerStream(Action? onDispose = null) : Stream
{
    private readonly ConcurrentQueue<byte[]> _chunks = new();
    private readonly SemaphoreSlim _signal = new(0);
    private volatile bool _completed;
    private Exception? _fault;
    private byte[]? _current; //正在读的块
    private int _currentOffset;
    private bool _disposed;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;

    /// <summary>生产者写一块；块按到达顺序拼起来就是完整音频字节</summary>
    public override void Write(byte[] buffer, int offset, int count) => Enqueue(buffer.AsSpan(offset, count).ToArray());

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Enqueue(buffer.ToArray());
        return ValueTask.CompletedTask;
    }

    /// <summary>生产者宣告结束；消费者读空后返回 0</summary>
    public void Complete()
    {
        _completed = true;
        _signal.Release();
    }

    /// <summary>生产者宣告失败；消费者读空已到的数据后抛这个异常</summary>
    public void Fail(Exception error)
    {
        _fault = error;
        Complete();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_current == null && _chunks.TryDequeue(out byte[]? next))
            {
                _current = next;
                _currentOffset = 0;
            }

            if (_current != null)
            {
                int count = Math.Min(_current.Length - _currentOffset, buffer.Length);
                _current.AsMemory(_currentOffset, count).CopyTo(buffer);
                _currentOffset += count;
                if (_currentOffset >= _current.Length) _current = null;
                return count;
            }

            if (_completed && _chunks.IsEmpty)
            {
                if (_fault is not null) throw _fault;
                return 0;
            }

            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    // 消费者提前释放（如打断朗读）时通知生产者停下
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            onDispose?.Invoke();
        }

        base.Dispose(disposing);
    }

    private void Enqueue(byte[] chunk)
    {
        if (chunk.Length == 0) return;
        _chunks.Enqueue(chunk);
        _signal.Release();
    }
}
