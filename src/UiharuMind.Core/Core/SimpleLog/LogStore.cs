/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;

namespace UiharuMind.Core.Core.SimpleLog;

/// <summary>
/// 日志存储引擎：<b>磁盘是唯一真相源</b>，内存只留索引项。
///
/// 两条流——<c>Log.txt</c> 是人类可直接打开的时间线主流，
/// 超过 <see cref="LogFormat.SpillThreshold"/> 的正文外置到 <c>Bodies.txt</c>，
/// 主流里那条只留摘要与引用。时序不因此断裂：条目仍在主流原位。
/// </summary>
public sealed class LogStore : IDisposable
{
    // 索引项本身几十字节,真正占地方的是预览串:400 字符最坏约 800 字节/条。
    // 5 万条在典型日志(首行多在百字上下)下约 8MB,全是长行的极端情况下约 40MB
    private const int MaxIndexEntries = 50_000;
    private const int IndexTrimBatch = 5_000; //超限后一次裁掉的条数,摊薄搬移开销

    private readonly LogFileWriter _main;
    private readonly LogFileWriter _bodies;
    private readonly List<LogIndexEntry> _index = new(256);
    private readonly object _locker = new();

    /// <summary>日志目录，「打开日志目录」按钮指向这里</summary>
    public string Directory { get; }

    public LogStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        DeleteLegacyFiles(directory);

        _main = new LogFileWriter(directory, LogFormat.MainBaseName, LogFormat.MainMaxBytes,
            LogFormat.MainGenerations);
        _bodies = new LogFileWriter(directory, LogFormat.BodiesBaseName, LogFormat.BodiesMaxBytes,
            LogFormat.BodiesGenerations);

        // 本次运行从干净的文件开始;上次运行的内容顺势变成第 1 代
        _main.RotateOnStartup();
        _bodies.RotateOnStartup();
        PurgeExpired(directory);
    }

    /// <summary>
    /// 落盘一条日志并登记索引
    /// </summary>
    /// <param name="item">日志条目</param>
    /// <returns>索引项</returns>
    public LogIndexEntry Append(LogItem item)
    {
        string header = LogFormat.Header(item);
        string preview = LogFormat.Preview(item.Text);
        int textByteLength = Encoding.UTF8.GetByteCount(item.Text);

        lock (_locker)
        {
            LogIndexEntry entry = item.Text.Length > LogFormat.SpillThreshold
                ? AppendSpilled(item, header, preview, textByteLength)
                : AppendInline(item, header, preview, textByteLength);

            _index.Add(entry);
            if (_index.Count > MaxIndexEntries) _index.RemoveRange(0, IndexTrimBatch);
            return entry;
        }
    }

    /// <summary>
    /// 读回一条日志的完整正文
    /// </summary>
    /// <param name="entry">索引项</param>
    /// <returns>正文；正文已被滚动淘汰（死链）时为 null</returns>
    public string? ReadText(LogIndexEntry entry)
    {
        lock (_locker)
        {
            LogFileWriter writer = entry.Stream == ELogStream.Bodies ? _bodies : _main;
            return writer.Read(entry.FileId, entry.Offset, entry.ByteLength);
        }
    }

    /// <summary>取当前索引的快照</summary>
    /// <returns>独立副本，调用方可自由遍历</returns>
    public List<LogIndexEntry> GetSnapshot()
    {
        lock (_locker) return new List<LogIndexEntry>(_index);
    }

    /// <summary>把两条流的缓冲都推给操作系统</summary>
    public void Flush()
    {
        lock (_locker)
        {
            _main.Flush();
            _bodies.Flush();
        }
    }

    /// <summary>清空索引与当前文件</summary>
    public void Clear()
    {
        lock (_locker)
        {
            _index.Clear();
            _main.Reset();
            _bodies.Reset();
        }
    }

    // 正文进主流:头行 + 原样正文 + 空行
    private LogIndexEntry AppendInline(LogItem item, string header, string preview, int textByteLength)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($"{header}\n{item.Text}\n\n");
        long start = _main.Append(bytes);
        long textOffset = start + Encoding.UTF8.GetByteCount(header) + 1; //跳过头行与它的换行
        return new LogIndexEntry(ELogStream.Main, _main.CurrentFileId, textOffset, textByteLength,
            item.LogType, item.Category, item.Time, preview);
    }

    // 正文进 Bodies,主流只留摘要与引用——这正是 Log.txt 能保持人类可读的原因
    private LogIndexEntry AppendSpilled(LogItem item, string header, string preview, int textByteLength)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes($"{item.Text}\n\n");
        long bodyOffset = _bodies.Append(bodyBytes);
        int bodyFileId = _bodies.CurrentFileId; //必须在 Append 之后读:它可能刚滚动过

        byte[] mainBytes = Encoding.UTF8.GetBytes(
            $"{header} -> {LogFormat.BodiesBaseName}.txt#{bodyOffset}\n{preview}\n\n");
        _main.Append(mainBytes);

        return new LogIndexEntry(ELogStream.Bodies, bodyFileId, bodyOffset, textByteLength,
            item.LogType, item.Category, item.Time, preview);
    }

    // 旧版是「退出时把全量快照序列化成 JSON 数组」,与新格式不兼容。它们只对上次运行有意义,不写迁移
    private static void DeleteLegacyFiles(string directory)
    {
        TryDelete(Path.Combine(directory, "LastLog.txt"));

        string main = Path.Combine(directory, $"{LogFormat.MainBaseName}.txt");
        if (!File.Exists(main)) return;
        try
        {
            // 新格式的头行是 "[2026-..",第二个字符必为数字;旧格式是 JSON 数组,'[' 之后是换行或空白
            using StreamReader reader = new(main);
            int first = reader.Read();
            int second = reader.Read();
            if (first == '[' && !char.IsDigit((char)second)) TryDelete(main);
        }
        catch (Exception)
        {
            // 认不出就留着,让滚动去处理
        }
    }

    private static void PurgeExpired(string directory)
    {
        DateTime deadline = DateTime.Now.AddDays(-LogFormat.RetentionDays);
        try
        {
            foreach (string path in System.IO.Directory.EnumerateFiles(directory, "*.txt"))
            {
                if (File.GetLastWriteTime(path) < deadline) TryDelete(path);
            }
        }
        catch (Exception)
        {
            // 清理失败不该影响启动
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // ignore
        }
    }

    public void Dispose()
    {
        lock (_locker)
        {
            _main.Dispose();
            _bodies.Dispose();
        }
    }
}
