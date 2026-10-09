namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 就地回调的进度。<see cref="Progress{T}"/> 会抓同步上下文投递，后台线程上直接回调即可，界面层自己切线程
/// </summary>
/// <param name="report">收到进度时调用</param>
public sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    /// <inheritdoc />
    public void Report(T value) => report(value);
}
