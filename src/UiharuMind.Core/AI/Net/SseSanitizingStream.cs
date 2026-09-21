using System.Text;
using UiharuMind.Core.Core.SimpleLog;

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
    private bool _sawDone; //是否见过 data: [DONE]——EOF 时没见过说明服务器掐了流
    private int _linesRead; //已读行数(诊断用:终止时看流到底被消费了多少)
    private string _lastDataLine = ""; //最后一条 data 行原文(诊断用:看服务端收尾时到底发了什么)

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
            string? line;
            try
            {
                line = _reader.ReadLine();
            }
            catch (OperationCanceledException) when (!CancellationToken.None.IsCancellationRequested)
            {
                Log.Warning("SSE read cancelled: the connection was likely dropped mid-stream.");
                throw;
            }
            catch (Exception e)
            {
                Log.Warning($"SSE read failed: {e.GetType().Name}: {e.Message}");
                throw;
            }

            if (line == null)
            {
                ReportEndIfAbnormal();
                return 0;
            }

            SetPending(line);
        }

        return CopyPending(buffer.AsSpan(offset, count));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (_offset >= _pending.Length)
        {
            string? line;
            try
            {
                line = await _reader.ReadLineAsync(cancellationToken);
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

            if (line == null)
            {
                ReportEndIfAbnormal();
                return 0;
            }

            SetPending(line);
        }

        return CopyPending(buffer.Span);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void SetPending(string line)
    {
        _linesRead++;
        if (line.Contains("[DONE]", StringComparison.Ordinal)) _sawDone = true;
        // 跳过 [DONE]:要的是最后一条<b>业务帧</b>(它才带 finish_reason/usage)。
        // 之前把 [DONE] 也记进来,收尾帧被它覆盖,看不到服务端到底发了什么
        if (!_sawDone && line.StartsWith("data:", StringComparison.Ordinal)) _lastDataLine = line;
        _pending = Encoding.UTF8.GetBytes(OpenAiCompatibleResponseFixer.FixEventStreamLine(line) + "\n");
        _offset = 0;
    }

    // 流读到 EOF 但始终没见过 [DONE]——正常的 SSE 结束必须带它。
    // 服务器静默掐连接时 .NET 这一侧不会抛异常，只会像这样正常读到末尾，
    // 不插这个哨兵的话这类故障永远没有痕迹。
    private void ReportEndIfAbnormal()
    {
        if (_sawDone) return;
        Log.Warning("SSE stream ended without [DONE]: the server dropped the connection mid-stream.");
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
        // 无条件留痕:无论流怎么结束(正常读完/被掐/被提前丢弃)都会走到这里。
        // 这是判断"服务端到底发没发完"的决定性观测——行数为 0 说明流根本没被读
        if (_sawDone)
            Log.Debug($"SseSanitizingStream disposed: {_linesRead} lines, saw [DONE] (normal end).");
        else
            Log.Warning($"SseSanitizingStream disposed: {_linesRead} lines, NO [DONE]. " +
                        (disposing ? "Disposed by caller" : "Finalizer"));

        // 诊断:最后一条 data 行原文。长思考后 text=0 时,这里能看到服务端收尾帧长什么样
        // (finish_reason 是 length 还是 stop、有没有 content 字段)
        if (_lastDataLine.Length > 0)
        {
            string preview = _lastDataLine.Length <= 2000
                ? _lastDataLine
                : _lastDataLine[..2000] + $"…(+{_lastDataLine.Length - 2000} chars)";
            Log.Debug($"SseSanitizingStream last data line: {preview}");
        }

        if (disposing)
        {
            _reader.Dispose();
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
