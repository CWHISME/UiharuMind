using System.Threading.Channels;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Usage;

/// <summary>
/// 用量账本的门面：<b>写走后台单写通道，读直连 SQLite</b>。
///
/// <para>
/// 埋点发生在请求路径上，那里一毫秒都不能等磁盘（<see cref="Append"/> 只往
/// <see cref="Channel{T}"/> 里丢一条，无锁、不碰 IO）；落盘由单写线程串行做，
/// 与 <c>LogManager</c> 是同一个范式。丢一条统计可以接受，卡住一次对话不可接受。
/// </para>
/// <para>
/// 库开在 <c>AppPaths.Data/Usage/Usage.db</c>（WAL）。<b>归 Data 而非 Cache</b>：这批数据
/// 不可再生——重跑一次推理是另一次推理，不是同一次，删了就是真的没了（ADR 0013）。
/// </para>
/// </summary>
public sealed class UsageJournal : IUsageStore, IDisposable
{
    private static volatile UsageJournal? _instance;
    private static readonly object InstanceLocker = new();

    private readonly Channel<UsageRecord> _channel =
        Channel.CreateUnbounded<UsageRecord>(new UnboundedChannelOptions { SingleReader = true });

    private readonly Task _writerLoop;
    private readonly UsageStore? _store; //开库失败时整块降级:统计丢了不拖累应用
    private int _dropped; //队列关闭后未写进的条数
    private long _written; //已落盘条数（退出时用它判断队列真的空了）
    private long _enqueued; //已入队条数

    /// <summary>进程内唯一的一份</summary>
    public static UsageJournal Instance
    {
        get
        {
            if (_instance != null) return _instance;
            lock (InstanceLocker) return _instance ??= new UsageJournal(AppPaths.Data.UsageDatabase);
        }
    }

    /// <summary>实例是否已经被创建过（退出时据此决定要不要 flush，不为一次没用过就建库）</summary>
    public static bool IsCreated => _instance != null;

    /// <inheritdoc />
    public event Action? Recorded;

    /// <summary>
    /// 打开用量库并起单写线程
    /// </summary>
    /// <param name="databasePath">数据库路径</param>
    public UsageJournal(string databasePath)
    {
        try
        {
            _store = new UsageStore(databasePath);
        }
        catch (Exception e)
        {
            Log.Warning($"Usage store init failed, usage records are dropped: {e.Message}");
        }

        _writerLoop = Task.Run(WriteLoopAsync);
    }

    /// <summary>
    /// 记一条请求终态。<b>非阻塞</b>：只入队，不等落盘
    /// </summary>
    /// <param name="record">一次请求的终态</param>
    public void Append(UsageRecord record)
    {
        if (_store == null) return;
        if (_channel.Writer.TryWrite(record))
        {
            Interlocked.Increment(ref _enqueued);
            return;
        }

        Interlocked.Increment(ref _dropped); //队列已关(进程正在退出)
    }

    /// <summary>概览查询</summary>
    public UsageSnapshot Query(UsageQuery query) => _store?.Query(query) ?? UsageSnapshot.Empty;

    /// <summary>明细分页</summary>
    public UsageDetailPage QueryDetails(UsageDetailQuery query) =>
        _store?.QueryDetails(query) ?? new UsageDetailPage([], 0);

    /// <summary>会话时间线</summary>
    public IReadOnlyList<UsageDetailRow> QuerySessionTimeline(string sessionId, long fromTs, long toTs) =>
        _store?.QuerySessionTimeline(sessionId, fromTs, toTs) ?? [];

    /// <summary>会话下拉</summary>
    public IReadOnlyList<SessionUsageSummary> ListSessions(long fromTs, long toTs, int limit = 100) =>
        _store?.ListSessions(fromTs, toTs, limit) ?? [];

    /// <summary>导出 CSV</summary>
    public long ExportCsv(UsageDetailQuery query, string path) => _store?.ExportCsv(query, path) ?? 0;

    /// <summary>模型下拉</summary>
    public IReadOnlyList<string> KnownModelNames(long fromTs, long toTs) =>
        _store?.KnownModelNames(fromTs, toTs) ?? [];

    /// <summary>保留策略：丢弃指定时刻之前的记录</summary>
    public int DeleteOlderThan(long cutoffTs) => _store?.DeleteOlderThan(cutoffTs) ?? 0;

    /// <summary>
    /// 等队列落完并关库。<b>只许退出路径调</b>（<c>App.Dispose</c> 里一次）。
    ///
    /// 关队之前先等一小段「队列既空又没有新写入」：退出时被取消的那一轮，它的收尾跑在别的
    /// 线程上，<c>SettleAllForShutdown</c> 返回时那一行 cancel 往往还没进来。不等这一下，
    /// 最后一条失败/取消记录就没了
    /// </summary>
    public void Shutdown()
    {
        WaitForIdle();
        _channel.Writer.TryComplete();
        try
        {
            if (!_writerLoop.Wait(TimeSpan.FromSeconds(3)))
                Log.Warning($"Usage journal did not drain in time; {Interlocked.CompareExchange(ref _dropped, 0, 0)} queued records may be lost.");
        }
        catch (Exception)
        {
            //退出路径上不再抛
        }

        _store?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose() => Shutdown();

    private async Task WriteLoopAsync()
    {
        await foreach (UsageRecord record in _channel.Reader.ReadAllAsync())
        {
            try
            {
                _store?.Insert(record);
                //订阅方是界面：它抛了也不许把单写线程带走（带走就一条都不再落盘）
                try
                {
                    Recorded?.Invoke();
                }
                catch (Exception e)
                {
                    Log.Warning($"Usage recorded handler failed: {e.Message}");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Usage write failed: {e.Message}");
            }
            finally
            {
                Interlocked.Increment(ref _written);
            }
        }
    }

    /// <summary>
    /// 等到入队数都落盘、且最近没有新写入（最多等 600ms）。
    /// <b>至少给 300ms</b>：退出时被取消的那一轮，它的 cancel 行要等 OCE 冒到 catch 里才入队，
    /// 队列一开始是空的，只看「空且没变化」会立即返回、把它漏掉。
    /// 不用 <c>ChannelReader.Count</c>：本实例的无界通道不支持它（调用抛 NotSupportedException）
    /// </summary>
    private void WaitForIdle()
    {
        DateTime start = DateTime.UtcNow;
        DateTime deadline = start.AddMilliseconds(600);
        long lastSeen = Interlocked.Read(ref _written);
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
            long written = Interlocked.Read(ref _written);
            bool minGrace = DateTime.UtcNow - start >= TimeSpan.FromMilliseconds(300);
            bool idle = written == lastSeen && written >= Interlocked.Read(ref _enqueued);
            if (minGrace && idle) return;
            lastSeen = written;
        }
    }
}
