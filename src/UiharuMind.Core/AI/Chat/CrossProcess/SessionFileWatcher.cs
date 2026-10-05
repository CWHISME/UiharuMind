namespace UiharuMind.Core.AI.Chat.CrossProcess;

/// <summary>
/// 监视会话目录里的会话头文件，攒一小会儿再把变过的会话标识成批交出去。
/// 一次写盘在系统层面是好几个事件（临时文件改名、写入、属性），逐个处理只是重复劳动
/// </summary>
internal sealed class SessionFileWatcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly FileSystemWatcher _watcher;
    private readonly string _suffix;
    private readonly Action<IReadOnlyCollection<string>> _onChanged;
    private readonly Timer _timer;
    private readonly HashSet<string> _pending = new();
    private readonly object _locker = new();
    private readonly object _flushLocker = new(); //两批不并发交出去:计时器可能在上一批还没处理完时又到点

    /// <summary>
    /// 开始监视
    /// </summary>
    /// <param name="directory">会话目录</param>
    /// <param name="suffix">会话头文件后缀，文件名去掉它就是会话标识</param>
    /// <param name="onChanged">一批变过的会话标识，来自线程池</param>
    public SessionFileWatcher(string directory, string suffix, Action<IReadOnlyCollection<string>> onChanged)
    {
        _suffix = suffix;
        _onChanged = onChanged;
        _timer = new Timer(_ => Flush());
        Directory.CreateDirectory(directory);
        _watcher = new FileSystemWatcher(directory, "*" + suffix)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += (_, e) => EnqueueChange(e.Name);
        _watcher.Changed += (_, e) => EnqueueChange(e.Name);
        _watcher.Deleted += (_, e) => EnqueueChange(e.Name);
        _watcher.Renamed += (_, e) =>
        {
            EnqueueChange(e.OldName);
            EnqueueChange(e.Name);
        };
        _watcher.EnableRaisingEvents = true;
    }

    // 记下这个文件对应的会话,重新起算防抖
    private void EnqueueChange(string? name)
    {
        if (name == null || !name.EndsWith(_suffix, StringComparison.Ordinal)) return;
        lock (_locker) _pending.Add(name[..^_suffix.Length]);
        _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    private void Flush()
    {
        List<string> batch;
        lock (_locker)
        {
            if (_pending.Count == 0) return;
            batch = _pending.ToList();
            _pending.Clear();
        }

        lock (_flushLocker) _onChanged(batch);
    }

    /// <summary>停止监视</summary>
    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }
}
