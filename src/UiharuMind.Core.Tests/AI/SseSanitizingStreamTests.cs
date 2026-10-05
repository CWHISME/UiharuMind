using System.Text;
using UiharuMind.Core.AI.Net;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// SSE 清洗流的分行与转发：行界在字节上找，跨读的半行、\r\n、比缓冲还长的行都要与按行读的结果一致
/// </summary>
public class SseSanitizingStreamTests
{
    private const string Chunk =
        "data: {\"id\":\"1\",\"choices\":[{\"index\":0,\"delta\":{\"reasoning\":\"想一想\"},\"finish_reason\":\"\"}]}";

    private const string FixedChunk =
        "data: {\"id\":\"1\",\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"想一想\"},\"finish_reason\":null}]}";

    /// <summary>\r\n 与裸 \r 都按换行认，输出统一成 \n；内层每次只给几个字节也不影响分行</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task LineEndings_AndSplitReads_YieldTheSameLines(string newline)
    {
        string sse = $": keep-alive{newline}{newline}{Chunk}{newline}{newline}data: [DONE]{newline}{newline}";

        string text = await ReadAllAsync(new TrickleStream(Encoding.UTF8.GetBytes(sse)));

        Assert.Equal($": keep-alive\n\n{FixedChunk}\n\ndata: [DONE]\n\n", text);
    }

    /// <summary>一整行比读缓冲还长（大段工具参数）：缓冲翻倍，整行原样转发</summary>
    [Fact]
    public async Task LineLongerThanBuffer_PassesThroughIntact()
    {
        string line = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"" + new string('长', 40_000) + "\"}}]}";

        string text = await ReadAllAsync(new MemoryStream(Encoding.UTF8.GetBytes(line + "\n\ndata: [DONE]\n\n")));

        Assert.Equal(line + "\n\ndata: [DONE]\n\n", text);
    }

    /// <summary>流尾最后一行没有换行：照常当一行转发（与按行读一致），补上换行</summary>
    [Fact]
    public async Task LastLineWithoutNewline_IsStillEmitted()
    {
        string text = await ReadAllAsync(new MemoryStream(Encoding.UTF8.GetBytes(Chunk + "\n\ndata: [DONE]")));

        Assert.Equal(FixedChunk + "\n\ndata: [DONE]\n", text);
    }

    /// <summary>同步读与异步读走同一套分行</summary>
    [Fact]
    public void SyncRead_MatchesAsyncRead()
    {
        using var stream = new SseSanitizingStream(new TrickleStream(Encoding.UTF8.GetBytes(Chunk + "\r\n\r\ndata: [DONE]\r\n\r\n")));
        using var reader = new StreamReader(stream);

        Assert.Equal(FixedChunk + "\n\ndata: [DONE]\n\n", reader.ReadToEnd());
    }

    private static async Task<string> ReadAllAsync(Stream inner)
    {
        await using var stream = new SseSanitizingStream(inner);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    // 每次只吐 1～7 个字节，专门制造跨读的半行与被拆开的 \r\n
    private sealed class TrickleStream(byte[] data) : Stream
    {
        private int _position;
        private int _turn;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int size = Math.Min(Math.Min(buffer.Length, _turn++ % 7 + 1), data.Length - _position);
            data.AsSpan(_position, size).CopyTo(buffer);
            _position += size;
            return size;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
