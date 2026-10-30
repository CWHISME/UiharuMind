/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Net;

namespace UiharuMind.Core.AI.Net.Transport;

/// <summary>
/// 切成 64KB 段的请求体（<see cref="HttpContent"/> 版）。切段逻辑在 <see cref="SegmentedBuffer"/>，
/// 与 <see cref="SegmentedBinaryContent"/> 是同一份实现——那个走 SDK 管道，这个走裸 <see cref="HttpClient"/>。
///
/// 内容只读，重试再发一遍同一份即可，序列化可重入。
/// </summary>
internal sealed class SegmentedHttpContent : HttpContent
{
    private readonly byte[][] _segments;
    private readonly int _length;

    /// <summary>
    /// 拷一份正文
    /// </summary>
    /// <param name="data">正文，调用返回后即可复用</param>
    public SegmentedHttpContent(ReadOnlySpan<byte> data)
    {
        _length = data.Length;
        _segments = SegmentedBuffer.Split(data);
    }

    /// <inheritdoc />
    protected override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }

    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SegmentedBuffer.WriteToAsync(_segments, stream, CancellationToken.None);

    /// <summary>
    /// 带取消令牌的那条重载。<b>必须覆盖</b>：不覆盖的话 <see cref="HttpClient"/> 走上面那条，
    /// 取消只能等整段写完才生效
    /// </summary>
    /// <param name="stream">目标流</param>
    /// <param name="context">传输上下文</param>
    /// <param name="cancellationToken">取消标记</param>
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
        CancellationToken cancellationToken) =>
        SegmentedBuffer.WriteToAsync(_segments, stream, cancellationToken);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
    }
}
