using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 按行转发 SSE 响应，逐行交给 <see cref="OpenAiCompatibleResponseFixer"/> 修正后再吐给下游。
/// 全程按字节：行界在字节上找，不需要修的行原样拷贝，需要修的行只改那几个字节，不解码成字符串、不建 DOM
/// （逐行解码再编码时，一次两千块的调用约产生 8MB 垃圾）。
/// 只做行缓冲，不整体缓冲：一次 Read 吐出已到手的全部整行、不等后续数据，流式输出的实时性不受影响。
/// 两块缓冲每个流各自持有、不从池里租：并发 Dispose 时池化缓冲可能被别人租走再写坏，省下的却只有每次调用 32KB。
/// </summary>
internal sealed class SseSanitizingStream : Stream
{
    private const int InitialBufferSize = 16 * 1024;

    private readonly Stream _inner;
    private readonly ArrayBufferWriter<byte> _output = new(InitialBufferSize); //处理好、待吐给下游的字节
    private byte[] _input = new byte[InitialBufferSize]; //读进来还没成行的字节
    private int _inputStart;
    private int _inputEnd;
    private int _outputRead;
    private bool _innerEnded;
    private bool _endReported;
    private bool _disposed;
    private bool _sawDone; //是否见过 data: [DONE]——EOF 时没见过说明服务器掐了流
    private int _usageFrames; //带 usage 的帧数:多于 1 时用量会被记多次
    private int _linesRead; //已读行数(诊断用:终止时看流到底被消费了多少)
    private byte[] _lastDataLine = []; //最后一条 data 行原文(诊断用:看服务端收尾时到底发了什么)
    private int _lastDataLineLength;

    public SseSanitizingStream(Stream inner)
    {
        _inner = inner;
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

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty) return 0;
        while (true)
        {
            if (TryDrain(buffer, out int copied)) return copied;
            if (TryProcessLines()) continue;
            if (_innerEnded)
            {
                if (FinishInput()) return 0;
                continue;
            }

            PrepareInput();
            int read;
            try
            {
                read = _inner.Read(_input, _inputEnd, _input.Length - _inputEnd);
            }
            catch (OperationCanceledException)
            {
                Log.Warning("SSE read cancelled: the connection was likely dropped mid-stream.");
                throw;
            }
            catch (Exception e)
            {
                Log.Warning($"SSE read failed: {e.GetType().Name}: {e.Message}");
                throw;
            }

            AcceptInput(read);
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))] //每块数据到达都会真的异步一次，状态机从池里取
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty) return 0;
        while (true)
        {
            if (TryDrain(buffer.Span, out int copied)) return copied;
            if (TryProcessLines()) continue;
            if (_innerEnded)
            {
                if (FinishInput()) return 0;
                continue;
            }

            PrepareInput();
            int read;
            try
            {
                read = await _inner.ReadAsync(_input.AsMemory(_inputEnd), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 非上层取消的读中断(超时/连接被掐):留个痕迹,否则这类故障永远无声
                Log.Warning("SSE read cancelled unexpectedly (not user stop): the connection was likely dropped mid-stream.");
                throw;
            }
            catch (Exception e)
            {
                Log.Warning($"SSE read failed: {e.GetType().Name}: {e.Message}");
                throw;
            }

            AcceptInput(read);
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private bool TryDrain(Span<byte> destination, out int copied)
    {
        int pending = _output.WrittenCount - _outputRead;
        if (pending == 0)
        {
            copied = 0;
            return false;
        }

        copied = Math.Min(pending, destination.Length);
        _output.WrittenSpan.Slice(_outputRead, copied).CopyTo(destination);
        _outputRead += copied;
        if (_outputRead == _output.WrittenCount)
        {
            _output.ResetWrittenCount();
            _outputRead = 0;
        }

        return true;
    }

    // 把缓冲里所有完整的行处理进输出。行尾的 \r 若是缓冲最后一个字节，要等下一块数据才知道是不是 \r\n
    private bool TryProcessLines()
    {
        bool processed = false;
        while (true)
        {
            ReadOnlySpan<byte> available = _input.AsSpan(_inputStart, _inputEnd - _inputStart);
            int index = available.IndexOfAny((byte)'\n', (byte)'\r');
            if (index < 0) return processed;

            int terminator = 1;
            if (available[index] == (byte)'\r')
            {
                if (index + 1 < available.Length)
                {
                    if (available[index + 1] == (byte)'\n') terminator = 2;
                }
                else if (!_innerEnded)
                {
                    return processed;
                }
            }

            ProcessLine(available[..index]);
            _inputStart += index + terminator;
            processed = true;
        }
    }

    private void ProcessLine(ReadOnlySpan<byte> line)
    {
        _linesRead++;
        if (line.IndexOf("[DONE]"u8) >= 0) _sawDone = true;
        if (line.IndexOf("\"usage\":{"u8) >= 0) _usageFrames++;
        // 跳过 [DONE]:要的是最后一条<b>业务帧</b>(它才带 finish_reason/usage)。
        // 之前把 [DONE] 也记进来,收尾帧被它覆盖,看不到服务端到底发了什么
        if (!_sawDone && line.StartsWith("data:"u8)) RememberLastDataLine(line);
        if (!OpenAiCompatibleResponseFixer.TryFixEventStreamLine(line, _output)) _output.Write(line);
        _output.Write("\n"u8);
    }

    private void RememberLastDataLine(ReadOnlySpan<byte> line)
    {
        if (_lastDataLine.Length < line.Length) _lastDataLine = new byte[Math.Max(line.Length, _lastDataLine.Length * 2)];
        line.CopyTo(_lastDataLine);
        _lastDataLineLength = line.Length;
    }

    // 腾出读入空间：处理过的前缀挪掉；一整行比缓冲还长（大段工具参数）时翻倍
    private void PrepareInput()
    {
        if (_inputStart > 0)
        {
            int pending = _inputEnd - _inputStart;
            _input.AsSpan(_inputStart, pending).CopyTo(_input);
            _inputStart = 0;
            _inputEnd = pending;
        }

        if (_inputEnd == _input.Length) Array.Resize(ref _input, _input.Length * 2);
    }

    private void AcceptInput(int read)
    {
        if (read == 0) _innerEnded = true;
        else _inputEnd += read;
    }

    // 内层读完：流尾没有换行的最后一行照常当一行（与 StreamReader.ReadLine 一致）。
    // 处理出了新内容返回 false，让调用方回去吐出；真正到头返回 true
    private bool FinishInput()
    {
        if (_inputStart < _inputEnd)
        {
            ProcessLine(_input.AsSpan(_inputStart, _inputEnd - _inputStart));
            _inputStart = _inputEnd;
            return false;
        }

        ReportEndIfAbnormal();
        return true;
    }

    // 流读到 EOF 但始终没见过 [DONE]——正常的 SSE 结束必须带它。
    // 服务器静默掐连接时 .NET 这一侧不会抛异常，只会像这样正常读到末尾，
    // 不插这个哨兵的话这类故障永远没有痕迹。
    private void ReportEndIfAbnormal()
    {
        if (_sawDone || _endReported) return;
        _endReported = true;
        Log.Warning("SSE stream ended without [DONE]: the server dropped the connection mid-stream.");
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        // 无条件留痕:无论流怎么结束(正常读完/被掐/被提前丢弃)都会走到这里。
        // 这是判断"服务端到底发没发完"的决定性观测——行数为 0 说明流根本没被读
        if (_sawDone)
            Log.Debug($"SseSanitizingStream disposed: {_linesRead} lines, {_usageFrames} usage frames, saw [DONE] (normal end).");
        else
            Log.Warning($"SseSanitizingStream disposed: {_linesRead} lines, NO [DONE]. " +
                        (disposing ? "Disposed by caller" : "Finalizer"));

        // 诊断:最后一条 data 行原文。长思考后 text=0 时,这里能看到服务端收尾帧长什么样
        // (finish_reason 是 length 还是 stop、有没有 content 字段)
        if (_lastDataLineLength > 0)
        {
            string line = Encoding.UTF8.GetString(_lastDataLine, 0, _lastDataLineLength);
            string preview = line.Length <= 2000 ? line : line[..2000] + $"…(+{line.Length - 2000} chars)";
            Log.Debug($"SseSanitizingStream last data line: {preview}");
        }

        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
