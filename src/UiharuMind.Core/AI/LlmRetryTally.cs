namespace UiharuMind.Core.AI;

/// <summary>
/// 一次服务调用内部的重发计数口。挂在 <see cref="LlmRequestContext"/> 上，
/// 由 <c>LazyChatClient</c> 每重发一次 +1，调用方在终态读出写进 <c>retry_count</c>。
///
/// <para>
/// 为什么是可变对象而不是一个数字：<see cref="LlmRequestContext"/> 是不可变快照，
/// 内层用 <c>Current with { … }</c> 复制时只换自己关心的字段，这个引用会被原样带下去——
/// 数字传不回来（AsyncLocal 里写下的值不向调用方回流），引用可以。
/// </para>
/// </summary>
public sealed class LlmRetryTally
{
    private int _retries;

    /// <summary>已重发的次数（首次尝试不算）</summary>
    public int Retries => Volatile.Read(ref _retries);

    /// <summary>记一次重发</summary>
    public void NoteRetry() => Interlocked.Increment(ref _retries);
}
