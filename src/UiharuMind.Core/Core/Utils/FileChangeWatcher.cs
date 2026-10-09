namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 文件在磁盘上的指纹：改时间、大小、在不在。两次指纹相同就当没变过
/// </summary>
/// <param name="Exists">文件是否存在</param>
/// <param name="LastWriteUtc">最后写入时间</param>
/// <param name="Length">字节数</param>
public readonly record struct FileStamp(bool Exists, DateTime LastWriteUtc, long Length)
{
    /// <summary>
    /// 取文件此刻的指纹
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>指纹；文件不存在或读不到时 <see cref="Exists"/> 为 false</returns>
    public static FileStamp Read(string path)
    {
        try
        {
            FileInfo info = new(path);
            return info.Exists ? new FileStamp(true, info.LastWriteTimeUtc, info.Length) : default;
        }
        catch (Exception)
        {
            return default;
        }
    }
}

/// <summary>
/// 盯一个文件，攒一小会儿再报一次。一次写盘在系统层面是好几个事件（临时文件改名、写入、属性），
/// 逐个报只是重复劳动；改名替换式的写入（先写临时文件再改名覆盖）也算这个文件变了。
/// 只报「可能变了」，变没变由调用方比 <see cref="FileStamp"/>
/// </summary>
public sealed class FileChangeWatcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly FileSystemWatcher _watcher;
    private readonly string _fileName;
    private readonly Timer _timer;

    /// <summary>
    /// 开始盯
    /// </summary>
    /// <param name="filePath">文件绝对路径</param>
    /// <param name="onChanged">文件可能变了，来自线程池</param>
    public FileChangeWatcher(string filePath, Action onChanged)
    {
        _fileName = Path.GetFileName(filePath);
        _timer = new Timer(_ => onChanged());
        // 盯目录而不是文件本身：文件被删了再建、被改名覆盖时，盯文件的句柄就断了
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(filePath)!)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += (_, e) => Enqueue(e.Name);
        _watcher.Changed += (_, e) => Enqueue(e.Name);
        _watcher.Deleted += (_, e) => Enqueue(e.Name);
        _watcher.Renamed += (_, e) =>
        {
            Enqueue(e.OldName);
            Enqueue(e.Name);
        };
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }

    private void Enqueue(string? name)
    {
        if (!string.Equals(name, _fileName, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // 释放与事件赛跑：已经不盯了
        }
    }
}
