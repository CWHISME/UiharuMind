namespace UiharuMind.Core.Core.Instances;

/// <summary>
/// 跨进程独占锁：独占打开一个锁文件，句柄在手就是持锁。
/// 进程死了系统自动关句柄，不会留下卡死的残锁——这也是不用「锁文件存在即上锁」的原因
/// </summary>
public static class ExclusiveFileLock
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// 试一次，拿不到立刻返回
    /// </summary>
    /// <param name="path">锁文件路径</param>
    /// <returns>持锁句柄，释放即放锁；被别人持着为 null</returns>
    public static IDisposable? TryAcquire(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 等到拿到为止，最多等 <paramref name="timeout"/>
    /// </summary>
    /// <param name="path">锁文件路径</param>
    /// <param name="timeout">最长等待</param>
    /// <returns>持锁句柄；超时为 null</returns>
    public static IDisposable? Acquire(string path, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            IDisposable? handle = TryAcquire(path);
            if (handle != null || DateTime.UtcNow >= deadline) return handle;
            Thread.Sleep(RetryInterval);
        }
    }
}
