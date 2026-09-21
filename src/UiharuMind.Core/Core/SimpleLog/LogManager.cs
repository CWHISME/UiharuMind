/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Threading.Channels;

namespace UiharuMind.Core.Core.SimpleLog;

/// <summary>
/// 日志入口。业务线程只负责把条目丢进 <see cref="Channel"/>（无锁、不阻塞、不碰 IO），
/// 由后台单写线程负责落盘与向订阅方派发。
///
/// 刷盘策略：<c>Error</c> 立即刷，其余靠 2 秒定时器兜底。
/// <b>刻意没有按缓冲大小刷盘的阈值</b>——主流条目都是几百字节，阈值永不触发，
/// 留着只会让人误以为它在起作用；真正的大正文走 <c>Bodies.txt</c>，每条写完即刷。
/// </summary>
public class LogManager
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    private static LogManager? _instance;
    private static readonly object Locker = new object();
    private static string? _defaultDirectory;

    public static LogManager Instance
    {
        get
        {
            if (_instance != null) return _instance;
            lock (Locker) return _instance ??= new LogManager(_defaultDirectory ?? AppPaths.Logs);
        }
    }

    /// <summary>
    /// 改掉单例的落盘位置。<b>只给测试用</b>，且必须在首次取 <see cref="Instance"/> 之前调用——
    /// 否则测试会写进并轮换用户真实的日志目录
    /// </summary>
    /// <param name="directory">日志目录</param>
    public static void UseDirectory(string directory) => _defaultDirectory = directory;

    private readonly LogStore? _store;
    private readonly Channel<LogItem> _channel =
        Channel.CreateUnbounded<LogItem>(new UnboundedChannelOptions { SingleReader = true });

    private long _enqueued; //入队序号
    private long _processed; //已写完序号,与入队序号之间的差就是在途条目

    private readonly Task _writerLoop;
    private readonly Task _flushLoop;
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>
    /// 日志落盘事件。来自<b>后台写入线程</b>，订阅方需自行派发到所需线程。
    /// 订阅方抛出的异常不会影响写入线程，也不会影响打日志的业务线程
    /// </summary>
    public event Action<LogIndexEntry>? OnLogAppended;

    public ILogger? Logger;

    /// <summary>日志目录</summary>
    public string Directory => _store?.Directory ?? AppPaths.Logs;

    public LogManager(string directory)
    {
        try
        {
            _store = new LogStore(directory);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Log store init failed, logs stay console-only: {e.Message}");
        }

        _writerLoop = Task.Run(WriteLoopAsync);
        _flushLoop = Task.Run(FlushLoopAsync);
    }

    public void Log(string str, ELogCategory category = ELogCategory.General)
    {
        LogItem item = new(ELogType.Log, str, category);
        Logger?.Debug(str, item);
        Enqueue(item);
    }

    public void LogWarning(string str, ELogCategory category = ELogCategory.General)
    {
        LogItem item = new(ELogType.Warning, str, category);
        Logger?.Warning(str, item);
        Enqueue(item);
    }

    public void LogError(string str, ELogCategory category = ELogCategory.General)
    {
        LogItem item = new(ELogType.Error, str, category);
        Logger?.Error(str, item);
        Enqueue(item);
    }

    /// <summary>取当前索引的快照</summary>
    /// <returns>独立副本，调用方可自由遍历</returns>
    public List<LogIndexEntry> GetSnapshot() => _store?.GetSnapshot() ?? [];

    /// <summary>
    /// 读回一条日志的完整正文
    /// </summary>
    /// <param name="entry">索引项</param>
    /// <returns>正文；正文已被滚动淘汰（死链）时为 null</returns>
    public string? ReadText(LogIndexEntry entry) => _store?.ReadText(entry);

    /// <summary>把缓冲推给操作系统。<b>只刷不轮换</b>——轮换只发生在写满上限与进程启动</summary>
    public void Flush()
    {
        WaitForDrain();
        _store?.Flush();
    }

    public void ClearLog()
    {
        WaitForDrain();
        _store?.Clear();
        Log("Clear Log!");
    }

    /// <summary>停止写入线程并落盘。只在进程退出时调用</summary>
    public void Shutdown()
    {
        _channel.Writer.TryComplete();
        try
        {
            _writerLoop.Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception)
        {
            // 退出路径上不再抛
        }

        _shutdown.Cancel();
        _store?.Flush();
        _store?.Dispose();
    }

    private void Enqueue(LogItem item)
    {
        Interlocked.Increment(ref _enqueued);
        // 队列已关(进程正在退出)就把序号补回来,否则 WaitForDrain 会空等到超时
        if (!_channel.Writer.TryWrite(item)) Interlocked.Increment(ref _processed);
    }

    /// 等到入队的都写完为止。<b>只等队列为空是不够的</b>——条目被取出但还没写完时，
    /// 队列已经是空的，崩溃路径上那条正好会漏掉
    private void WaitForDrain()
    {
        long target = Interlocked.Read(ref _enqueued);
        SpinWait spin = new();
        while (Interlocked.Read(ref _processed) < target)
        {
            if (_writerLoop.IsCompleted) return; //写入线程已经走了,再等也没有意义
            spin.SpinOnce();
        }
    }

    private async Task WriteLoopAsync()
    {
        await foreach (LogItem item in _channel.Reader.ReadAllAsync())
        {
            LogIndexEntry? entry = null;
            try
            {
                entry = _store?.Append(item);
                if (item.LogType == ELogType.Error) _store?.Flush(); //错误必须落地:它下一秒可能就崩了
            }
            catch (Exception e)
            {
                Console.WriteLine($"Log write failed: {e.Message}"); //不能再走 Log,会递归
            }

            // 计数必须在派发之前推进:订阅方可能很慢,而 WaitForDrain 等的是"落盘完成"
            Interlocked.Increment(ref _processed);

            if (entry == null) continue;
            try
            {
                OnLogAppended?.Invoke(entry);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Log subscriber failed: {e.Message}");
            }
        }
    }

    private async Task FlushLoopAsync()
    {
        try
        {
            using PeriodicTimer timer = new(FlushInterval);
            while (await timer.WaitForNextTickAsync(_shutdown.Token)) _store?.Flush();
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
    }
}
