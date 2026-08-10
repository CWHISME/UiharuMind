using System.Net;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 包装 SSE 响应体，读取时逐行修正，保持流式不整体缓冲。
/// </summary>
internal sealed class SseSanitizingContent : HttpContent
{
    private readonly HttpContent _inner;

    public SseSanitizingContent(HttpContent inner)
    {
        _inner = inner;
        foreach (var header in inner.Headers)
        {
            // 内容被改写后长度不再准确，Content-Length 不能照抄
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        await using var source = new SseSanitizingStream(await _inner.ReadAsStreamAsync());
        await source.CopyToAsync(stream);
    }

    protected override async Task<Stream> CreateContentReadStreamAsync()
        => new SseSanitizingStream(await _inner.ReadAsStreamAsync());

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
