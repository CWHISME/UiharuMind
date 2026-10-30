/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Net;
using System.Net.Http;

namespace UiharuMind.Core.AI.Net.Transport;

/// <summary>
/// 分段的请求体（<see cref="HttpContent"/> 版）：每段小于 85000 字节，不进大对象堆，
/// 几十万字的请求体也只是一串小数组。
///
/// <para>与 <see cref="SegmentedBinaryContent"/> 同构——那个是 SDK 管道用的
/// （<c>System.ClientModel.BinaryContent</c>），这个是 HttpClient 用的
/// （<see cref="HttpContent"/>）。两条链路的请求体都要分段：几十万字的会话
/// 一轮下来就是几百 KB 的大对象堆分配，而这类请求正是最频繁的那一种。</para>
///
/// <para>内容只读，重试再发一遍同一份即可，<see cref="SerializeToStreamAsync"/> 可重入。</para>
/// </summary>
internal sealed class SegmentedHttpContent : HttpContent
{
    private const int SegmentSize = 64 * 1024;

    private readonly byte[][] _segments;
    private readonly int _length;

    /// <summary>
    /// 拷一份正文
    /// </summary>
    /// <param name="data">正文，调用返回后即可复用</param>
    public SegmentedHttpContent(ReadOnlySpan<byte> data)
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
    protected override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }

    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        foreach (byte[] segment in _segments) stream.Write(segment);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
    }
}
