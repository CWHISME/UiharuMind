using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Instances;

/// <summary>
/// 同一档案下的多个实例（ADR 0064）。先起的拿到主实例锁，后台活（定时任务）只归它
/// </summary>
public static class AppInstance
{
    private static IDisposable? _primaryLock; //持到进程结束,不释放
    private static bool _claimed;
    private static bool _queried; //认领之前有没有人问过 IsPrimary:有就是启动顺序错了

    /// <summary>运行期文件目录：控制通道、进程标记、会话租约都在这里</summary>
    public static string RunDirectory => Path.Combine(AppPaths.Root, "run");

    /// <summary>每个活着的实例在这里留一个进程标记（见 <c>UncleanExitGuard</c>）</summary>
    public static string InstancesDirectory => Path.Combine(RunDirectory, "instances");

    /// <summary>
    /// 是否主实例。没认领过（测试、命令行）一律算主实例，行为与单实例时代一致
    /// </summary>
    public static bool IsPrimary
    {
        get
        {
            if (!_claimed) _queried = true;
            return !_claimed || _primaryLock != null;
        }
    }

    /// <summary>
    /// 认领主实例。应用启动时调一次，要早于任何按 <see cref="IsPrimary"/> 分叉的组件
    /// </summary>
    /// <returns>是否拿到了主实例</returns>
    public static bool ClaimPrimary()
    {
        if (_claimed) return IsPrimary;
        _claimed = true;
        // 先问后认领:问的那个组件已经按「主实例」定了型(调度后端在构造时就分叉),非主实例上它会照跑后台活
        if (_queried) Log.Warning("AppInstance.IsPrimary was read before ClaimPrimary; a component may have assumed primary.");
        _primaryLock = ExclusiveFileLock.TryAcquire(Path.Combine(RunDirectory, "primary.lock"));
        return _primaryLock != null;
    }
}
