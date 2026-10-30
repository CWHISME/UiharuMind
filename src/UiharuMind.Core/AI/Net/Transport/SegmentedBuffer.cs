namespace UiharuMind.Core.AI.Net.Transport;

/// <summary>
/// 把一份正文切成 64KB 段，以及按段写出去。
///
/// <para>段小于 85000 字节就不进大对象堆——几十万字的会话一轮下来是几百 KB 的大对象堆分配，
/// 而这类请求正是最频繁的那一种。</para>
///
/// <para>段归 GC 管、<b>不从池里租</b>：HTTP/2 可能在请求体发完之前就收到响应，
/// 那时还池会把别处的数据发出去。</para>
///
/// <para>两条链路各有一个 <c>HttpContent</c> / <c>BinaryContent</c> 包装
/// （<see cref="SegmentedHttpContent"/> / <see cref="SegmentedBinaryContent"/>），
/// 因为基类不同没法共用类型——但切段与写出<b>只有这一份实现</b>。</para>
/// </summary>
internal static class SegmentedBuffer
{
    /// <summary>段大小。64KB 远小于 85000 的大对象堆门槛</summary>
    public const int SegmentSize = 64 * 1024;

    /// <summary>
    /// 拷一份正文并切成段
    /// </summary>
    /// <param name="data">正文，调用返回后即可复用</param>
    /// <returns>按顺序排列的段</returns>
    public static byte[][] Split(ReadOnlySpan<byte> data)
    {
        byte[][] segments = new byte[(data.Length + SegmentSize - 1) / SegmentSize][];
        for (int i = 0; i < segments.Length; i++)
        {
            int size = Math.Min(SegmentSize, data.Length);
            segments[i] = GC.AllocateUninitializedArray<byte>(size);
            data[..size].CopyTo(segments[i]);
            data = data[size..];
        }

        return segments;
    }

    /// <summary>
    /// 按顺序把段写出去
    /// </summary>
    /// <param name="segments">段</param>
    /// <param name="stream">目标流</param>
    /// <param name="cancellationToken">取消标记</param>
    public static async Task WriteToAsync(IReadOnlyList<byte[]> segments, Stream stream,
        CancellationToken cancellationToken)
    {
        foreach (byte[] segment in segments)
        {
            await stream.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
        }
    }
}
