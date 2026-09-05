using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Tests.SimpleLog;

/// <summary>
/// <see cref="LogManager"/> 的保留上限与快照语义。钉住的事实：日志列表有界、
/// 淘汰的是最早的那批、快照与内部列表互不影响。
/// <para>
/// 上限缺失是原实现的真实缺陷——<c>_logItems</c> 是个只增不减的 <c>List</c>，
/// 进程活多久它就长多久，日志页那棵常驻视觉树跟着一起长。
/// </para>
/// <para>
/// 只能拿生产单例做靶子：<c>Instance</c> 是静态字段，拿不到干净实例。
/// 这里的断言都对「之前已有多少条」不敏感，所以不依赖测试执行顺序。
/// </para>
/// </summary>
public class LogManagerTests
{
    /// <summary>
    /// 超过上限后列表保持有界，且留下的是最新的那些
    /// </summary>
    [Fact]
    public void AddLog_BeyondCap_KeepsNewestAndStaysBounded()
    {
        const int pushCount = 20000;
        string firstMarker = $"first-{Guid.NewGuid():N}";
        string lastMarker = $"last-{Guid.NewGuid():N}";

        LogManager.Instance.Log(firstMarker);
        for (int i = 0; i < pushCount; i++) LogManager.Instance.Log($"filler-{i}");
        LogManager.Instance.Log(lastMarker);

        List<LogItem> snapshot = LogManager.Instance.GetSnapshot();

        Assert.True(snapshot.Count <= 5000, $"日志列表未被裁剪，实际 {snapshot.Count} 条");
        Assert.Contains(snapshot, x => x.LogString.Contains(lastMarker));
        Assert.DoesNotContain(snapshot, x => x.LogString.Contains(firstMarker));
    }

    /// <summary>
    /// 快照是副本：改动它不会碰到内部列表，遍历它也不会与写入线程竞争
    /// </summary>
    [Fact]
    public void GetSnapshot_ReturnsIndependentCopy()
    {
        LogManager.Instance.Log($"snapshot-{Guid.NewGuid():N}");

        List<LogItem> first = LogManager.Instance.GetSnapshot();
        int countBefore = first.Count;
        first.Clear();

        List<LogItem> second = LogManager.Instance.GetSnapshot();

        Assert.NotSame(first, second);
        Assert.Equal(countBefore, second.Count);
    }

    /// <summary>
    /// 订阅方抛异常不该把锁带走。原实现在锁内触发事件且没有 try/finally，
    /// 一次异常就会让后续所有打日志的线程永久自旋在 SpinLock 上
    /// </summary>
    [Fact]
    public void AddLog_SubscriberThrows_DoesNotDeadlockLaterCalls()
    {
        Action<LogItem> thrower = _ => throw new InvalidOperationException("boom");
        LogManager.Instance.OnLogChange += thrower;

        try
        {
            Assert.Throws<InvalidOperationException>(() => LogManager.Instance.Log("throwing-subscriber"));
        }
        finally
        {
            LogManager.Instance.OnLogChange -= thrower;
        }

        // 锁已释放才走得到这里；原实现会卡死在这一句
        string marker = $"after-throw-{Guid.NewGuid():N}";
        LogManager.Instance.Log(marker);
        Assert.Contains(LogManager.Instance.GetSnapshot(), x => x.LogString.Contains(marker));
    }
}
