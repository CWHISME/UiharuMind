using UiharuMind.Core.Core.Instances;

namespace UiharuMind.Core.AI.Chat.CrossProcess;

/// <summary>
/// 会话租约：同一个会话同一时刻只在一个实例里跑轮次（ADR 0064）。
/// 进程内按引用计数——群轮与私聊、唤醒轮这些本进程内的并存由各自的闸管，这里只拦别的实例
/// </summary>
internal static class SessionLease
{
    // 别人「探一下」也是真的取一下锁,撞上那几微秒就会误判:取锁、探测都容一小会儿
    private static readonly TimeSpan ContentionGrace = TimeSpan.FromMilliseconds(100);

    private static readonly object Locker = new();
    private static readonly Dictionary<string, Held> HeldLeases = new();

    private sealed class Held
    {
        /// <summary>锁文件的独占句柄</summary>
        public required IDisposable Handle;

        /// <summary>本进程内还有几处持着</summary>
        public int Count;
    }

    /// <summary>租约文件所在目录</summary>
    public static string Directory => Path.Combine(AppInstance.RunDirectory, "leases");

    /// <summary>
    /// 取这个会话的租约
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>归还用的句柄；被别的实例持着为 null</returns>
    public static IDisposable? TryAcquire(string sessionId)
    {
        lock (Locker)
        {
            if (!HeldLeases.TryGetValue(sessionId, out Held? held))
            {
                IDisposable? handle = ExclusiveFileLock.Acquire(LeasePath(sessionId), ContentionGrace);
                if (handle == null) return null;
                held = new Held { Handle = handle };
                HeldLeases[sessionId] = held;
            }

            held.Count++;
        }

        return new Scope(sessionId);
    }

    /// <summary>
    /// 是否被别的实例持着。本进程自己持着的不算
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>是否被别的实例持着</returns>
    public static bool IsHeldElsewhere(string sessionId)
    {
        lock (Locker)
        {
            if (HeldLeases.ContainsKey(sessionId)) return false;
        }

        using IDisposable? probe = ExclusiveFileLock.Acquire(LeasePath(sessionId), ContentionGrace);
        if (probe != null) return false;
        // 查完本进程到探测之间,本进程别的线程可能刚取到它:那是自己持着,不是别人
        lock (Locker)
        {
            return !HeldLeases.ContainsKey(sessionId);
        }
    }

    private static string LeasePath(string sessionId) => Path.Combine(Directory, sessionId + ".lock");

    private static void Release(string sessionId)
    {
        lock (Locker)
        {
            if (!HeldLeases.TryGetValue(sessionId, out Held? held)) return;
            if (--held.Count > 0) return;
            HeldLeases.Remove(sessionId);
            held.Handle.Dispose();
        }
    }

    private sealed class Scope(string sessionId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) Release(sessionId);
        }
    }
}
