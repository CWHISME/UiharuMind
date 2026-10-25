using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat.WakeLoops;

/// <summary>后台唤醒循环（离席、目标模式）共用的小件：等延迟、取产物指纹、安全地发事件</summary>
internal static class WakeLoop
{
    /// <summary>等一段延迟（负数按 0）。<b>永不同步完成</b>：返回前至少让出一个异步边界</summary>
    /// <param name="delay">等待的实现（测试替换）</param>
    /// <param name="duration">等多久</param>
    /// <param name="token">取消令牌</param>
    /// <returns>等到了为 true；被取消为 false</returns>
    public static async Task<bool> WaitAsync(Func<TimeSpan, CancellationToken, Task> delay, TimeSpan duration,
        CancellationToken token)
    {
        try
        {
            // 先让出一个异步边界再等：Task.Delay(0) 是同步完成的，而排程方可能是静态初始化——
            // GoalModeController 的 ctor 里 Restore→排下次唤醒，到点即触发的那一轮会整个跑在
            // 静态初始化的同步栈里，那时 Instance 字段还没赋值，唤醒轮里重入
            // GoalModeController.Instance 的 lambda（App 装配的 StreakExemptSource）拿到的是
            // null，踩空抛 NRE，那一轮被 SessionWakeTurn 的 catch 吞掉、补唤醒静默丢失
            // （2026-10-25 实机：重启后 goal-mode 补 Wake turn failed，cause=goal-mode NRE）。
            // 让出之后唤醒轮只可能在排程方返回之后跑，重入永远见得到完整的单例
            await Task.Yield();
            await delay(duration < TimeSpan.Zero ? TimeSpan.Zero : duration, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>产物指纹 = 件数 + 最近一次改动：新建与再次改动都算有新产物</summary>
    /// <param name="artifacts">产物</param>
    /// <returns>指纹</returns>
    public static string StampOf(IReadOnlyList<GroupArtifact> artifacts)
    {
        DateTimeOffset latest = artifacts.Count == 0 ? DateTimeOffset.MinValue : artifacts.Max(x => x.LastWrite);
        return $"{artifacts.Count}|{latest.UtcTicks}";
    }

    /// <summary>取产物指纹；读盘失败按空指纹算（只影响进展判定）</summary>
    /// <param name="stamp">指纹的取法</param>
    /// <param name="session">会话</param>
    /// <returns>指纹</returns>
    public static string SafeStamp(Func<ChatSession, string> stamp, ChatSession session)
    {
        try
        {
            return stamp(session);
        }
        catch (Exception e)
        {
            Log.Warning($"Wake loop artifact stamp failed: session={session.SessionId}: {e.Message}");
            return string.Empty;
        }
    }

    /// <summary>发事件：订阅方抛错只记日志，不打断循环</summary>
    /// <param name="handler">事件</param>
    /// <param name="arg">参数</param>
    /// <param name="what">日志里怎么称呼这个事件</param>
    public static void Raise<T>(Action<T>? handler, T arg, string what)
    {
        try
        {
            handler?.Invoke(arg);
        }
        catch (Exception e)
        {
            Log.Error($"{what} handler failed: {e}");
        }
    }
}
