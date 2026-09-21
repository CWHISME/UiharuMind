using SharpHook.Data;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Input;

/// <summary>
/// 全局快捷键的注册表与匹配器。
/// <para>
/// 写在 UI 线程（设置里改快捷键会整批重注册），读在钩子线程，且读远多于写，
/// 故采用写时复制：读取方只取一次数组引用即可遍历，无锁、也不可能撞上并发修改。
/// </para>
/// <para>
/// 匹配是**排他**的：按下的修饰键必须与注册的完全一致（左右不分），于是 Ctrl+A 与 Ctrl+Shift+A
/// 可以共存互不抢占，也不会出现「多按一个键照样触发」。
/// </para>
/// </summary>
internal sealed class ShortcutRegistry
{
    private readonly object _writeLock = new();

    private KeyCombinationData[] _combinations = [];
    private int _suspendCount;

    /// <summary>快捷键分发是否被挂起（录制快捷键期间）</summary>
    public bool IsSuspended => Volatile.Read(ref _suspendCount) > 0;

    public void Register(KeyCombinationData combination)
    {
        lock (_writeLock)
        {
            var existing = Array.Find(_combinations, item => item.Conflicts(combination));
            if (existing != null)
            {
                // 匹配按注册顺序取首个命中，故先注册的那条会一直吃掉这个组合键
                Log.Warning($"Shortcut '{combination.Name}' uses the same keys as '{existing.Name}' and will never fire.");
            }

            Volatile.Write(ref _combinations, [.._combinations, combination]);
        }
    }

    public void Unregister(KeyCombinationData combination)
    {
        lock (_writeLock)
        {
            if (Array.IndexOf(_combinations, combination) < 0) return;
            Volatile.Write(ref _combinations, Array.FindAll(_combinations, item => item != combination));
        }
    }

    public void Clear()
    {
        lock (_writeLock) Volatile.Write(ref _combinations, []);
    }

    /// <summary>
    /// 挂起快捷键分发，直到返回的句柄被释放。嵌套挂起按计数配对。
    /// </summary>
    /// <returns>释放即恢复分发的句柄</returns>
    public IDisposable Suspend()
    {
        Interlocked.Increment(ref _suspendCount);
        return new SuspendScope(this);
    }

    /// <summary>
    /// 匹配并触发快捷键
    /// </summary>
    /// <param name="keyCode">刚按下的主键</param>
    /// <param name="modifiers">此刻按下的修饰键位集</param>
    /// <returns>命中并触发返回 True（调用方据此决定是否吞掉该按键）</returns>
    public bool TryTrigger(KeyCode keyCode, EModifierKeys modifiers)
    {
        if (IsSuspended) return false;

        var combinations = Volatile.Read(ref _combinations);
        var folded = modifiers.Fold();

        foreach (var combination in combinations)
        {
            if (!combination.Matches(keyCode, folded)) continue;
            try
            {
                combination.OnTrigger.Invoke();
            }
            catch (Exception e)
            {
                Log.Warning($"Shortcut '{combination.Name}' failed: {e.Message}");
            }

            return true;
        }

        return false;
    }

    private void Resume()
    {
        Interlocked.Decrement(ref _suspendCount);
    }

    private sealed class SuspendScope(ShortcutRegistry registry) : IDisposable
    {
        private ShortcutRegistry? _registry = registry;

        public void Dispose()
        {
            // 幂等：调用方普遍写成 scope?.Dispose() 后置 null，但重复释放不该把计数减穿
            var registry = Interlocked.Exchange(ref _registry, null);
            registry?.Resume();
        }
    }
}
