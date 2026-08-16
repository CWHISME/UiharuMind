using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.Tests.Singletons;

/// <summary>
/// <see cref="Singleton{T}"/> 的发布顺序。钉住的事实：并发首次取用只可能拿到
/// 同一个<b>已初始化完成</b>的实例。旧实现先发布再初始化，这里必红。
/// </summary>
public class SingletonTests
{
    /// <summary>
    /// 靶子只给本测试用。<c>_instance</c> 是按封闭泛型类型各自一份的静态字段，
    /// 拿生产单例当靶子等于污染全局状态，而且「首次取用」全进程只有一次、复现不了。
    /// 初始化里刻意慢一拍，把「已发布但尚未初始化完」的窗口放大到必现。
    /// </summary>
    private class SlowInitTarget : Singleton<SlowInitTarget>, IInitialize
    {
        private static int _constructedCount;

        /// <summary>构造次数。环或自环导致的二次构造会让它大于 1</summary>
        public static int ConstructedCount => Volatile.Read(ref _constructedCount);

        /// <summary>初始化是否已跑完</summary>
        public bool IsInitialized { get; private set; }

        public SlowInitTarget()
        {
            Interlocked.Increment(ref _constructedCount);
        }

        public void OnInitialize()
        {
            Thread.Sleep(50); //放大半初始化窗口
            IsInitialized = true;
        }
    }

    [Fact]
    public void ConcurrentFirstAccessGetsSameInitializedInstance()
    {
        const int threadCount = 16;
        var results = new SlowInitTarget?[threadCount];
        // 必须在「拿到手的那一刻」采样:实例是共享的,等线程都结束再读 IsInitialized
        // 只会看到早已初始化完的同一个对象,半成品那一瞬就被抹掉了
        var initializedOnAcquire = new bool[threadCount];
        var failures = new Exception?[threadCount];
        var threads = new Thread[threadCount];

        // 用真线程而非 Parallel.For:后者的并行度由线程池决定,凑不满 Barrier 的参与者会直接卡死
        using var startLine = new Barrier(threadCount);
        for (int i = 0; i < threadCount; i++)
        {
            int index = i;
            threads[i] = new Thread(() =>
            {
                try
                {
                    startLine.SignalAndWait();
                    SlowInitTarget instance = SlowInitTarget.Instance;
                    results[index] = instance;
                    initializedOnAcquire[index] = instance.IsInitialized;
                }
                catch (Exception e)
                {
                    failures[index] = e;
                }
            });
            threads[i].Start();
        }

        foreach (Thread thread in threads)
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "取用单例的线程未在超时内结束");
        }

        Assert.All(failures, Assert.Null);
        Assert.All(results, x =>
        {
            Assert.NotNull(x);
            Assert.Same(results[0], x);
        });
        Assert.All(initializedOnAcquire, x => Assert.True(x, "拿到了尚未初始化完成的实例"));
        Assert.Equal(1, SlowInitTarget.ConstructedCount);
    }
}
