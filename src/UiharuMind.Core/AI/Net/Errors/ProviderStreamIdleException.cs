namespace UiharuMind.Core.AI.Net.Errors;

/// <summary>
/// 流开着、但安静得太久：连接没断、也没报错，就是一个字节都不来。
///
/// <para>
/// 继承 <see cref="IOException"/> 是刻意的：判据交给 <see cref="TransportFault.IsRetryableDrop"/>，
/// 处置与「流被掐断」完全一样（整段重发）。半开的 TCP 在睡眠/换网之后就是这么表现的，
/// 而 <c>ChatClientFactory</c> 把读时限关掉了（<c>NetworkTimeout = Infinite</c>），
/// 没有这一道的话那次读会永远挂着——轮次不结束、执行者闸门不放、界面一直显示「运行中」。
/// </para>
/// </summary>
internal sealed class ProviderStreamIdleException : IOException
{
    /// <summary>
    /// 静默上限
    /// </summary>
    public TimeSpan IdleLimit { get; }

    /// <summary>超时前已经读到的行数（诊断：0 行说明连首帧都没来）</summary>
    public int LinesRead { get; }

    /// <param name="idleLimit">静默上限</param>
    /// <param name="linesRead">已读行数</param>
    public ProviderStreamIdleException(TimeSpan idleLimit, int linesRead)
        : base($"stream went silent for {idleLimit.TotalSeconds:0}s after {linesRead} line(s)")
    {
        IdleLimit = idleLimit;
        LinesRead = linesRead;
    }
}
